define(['baseView', 'loading', 'responseHelper', 'emby-input', 'emby-button'], function (BaseView, loading, responseHelper) {
    'use strict';

    // Must match CrateDiggerPlugin.PluginGuid.
    var PluginUniqueId = '9b2f7a1c-5d4e-4a68-b3f1-8c0e2d7f6a54';

    var STAGE_ORDER = ['AnalyzingLibrary', 'Thinking', 'MatchingTracks', 'CreatingPlaylist'];
    var STAGE_LABELS = {
        Queued: 'Queued',
        AnalyzingLibrary: 'Analyzing library',
        Thinking: 'Thinking',
        MatchingTracks: 'Matching tracks',
        CreatingPlaylist: 'Creating playlist',
        Completed: 'Done'
    };

    function sendToast(text) {
        require(['toast'], function (toast) {
            toast(text);
        });
    }

    function escapeHtml(s) {
        return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
        });
    }

    function log() {
        // F12 console breadcrumb — makes remote debugging of this page possible.
        try {
            console.log.apply(console, ['[CrateDigger]'].concat(Array.prototype.slice.call(arguments)));
        } catch (err) { /* no console */ }
    }

    function errText(err) {
        if (!err) return 'unknown error';
        if (err.status) return 'HTTP ' + err.status + (err.statusText ? ' ' + err.statusText : '');
        return err.message || String(err);
    }

    // ApiClient.ajax may hand back the raw response string instead of a parsed
    // object — normalize defensively (this bit us once: resp.JobId was undefined,
    // so no status poll ever started and the button stayed disabled).
    function toJson(value) {
        if (value == null) return null;
        if (typeof value === 'object') return value;
        try {
            return JSON.parse(value);
        } catch (err) {
            return null;
        }
    }

    function View(view, params) {
        BaseView.apply(this, arguments);

        this._pollTimer = null;
        this._generating = false;

        view.querySelector('form.cdSettings').addEventListener('submit', this.onSubmit.bind(this));
        view.querySelector('.btnGenerate').addEventListener('click', this.onGenerate.bind(this));
    }

    Object.assign(View.prototype, BaseView.prototype);

    // ---------- settings ----------

    View.prototype.onSubmit = function (e) {
        e.preventDefault();

        var view = this.view;
        var instance = this;

        loading.show();

        ApiClient.getPluginConfiguration(PluginUniqueId).then(function (config) {
            config.ApiKey = (view.querySelector('.cdApiKey').value || '').trim();
            config.BaseUrl = (view.querySelector('.cdBaseUrl').value || '').trim();
            config.Model = (view.querySelector('.cdModel').value || '').trim();
            config.MaxPlaylistTracks = parseInt(view.querySelector('.cdMaxTracks').value, 10) || 30;
            config.MatchThreshold = parseFloat(view.querySelector('.cdThreshold').value);
            if (isNaN(config.MatchThreshold)) {
                config.MatchThreshold = 0.75;
            }
            config.LlmTimeoutSeconds = parseInt(view.querySelector('.cdTimeout').value, 10) || 300;
            config.LlmMaxTokens = parseInt(view.querySelector('.cdMaxTokens').value, 10);
            if (isNaN(config.LlmMaxTokens)) {
                config.LlmMaxTokens = 8192;
            }
            config.LlmExtraJson = (view.querySelector('.cdExtraJson').value || '').trim();
            try {
                if (config.LlmExtraJson) {
                    JSON.parse(config.LlmExtraJson);
                }
            } catch (err) {
                loading.hide();
                sendToast('Extra request JSON is not valid JSON');
                return;
            }

            ApiClient.updatePluginConfiguration(PluginUniqueId, config).then(function () {
                loading.hide();
                sendToast('Settings saved');
            }, function (err) {
                loading.hide();
                responseHelper.handleErrorResponse(err);
            });
        }, responseHelper.handleErrorResponse);
    };

    View.prototype.load = function () {
        var view = this.view;

        ApiClient.getPluginConfiguration(PluginUniqueId).then(function (config) {
            view.querySelector('.cdApiKey').value = config.ApiKey || '';
            view.querySelector('.cdBaseUrl').value = config.BaseUrl || 'https://api.openai.com/v1';
            view.querySelector('.cdModel').value = config.Model || 'gpt-4o-mini';
            view.querySelector('.cdMaxTracks').value = config.MaxPlaylistTracks || 30;
            view.querySelector('.cdThreshold').value =
                typeof config.MatchThreshold === 'number' ? config.MatchThreshold : 0.75;
            view.querySelector('.cdTimeout').value = config.LlmTimeoutSeconds || 300;
            view.querySelector('.cdMaxTokens').value = typeof config.LlmMaxTokens === 'number' ? config.LlmMaxTokens : 8192;
            view.querySelector('.cdExtraJson').value = config.LlmExtraJson || '';
        }, responseHelper.handleErrorResponse);
    };

    View.prototype.onResume = function (options) {
        BaseView.prototype.onResume.apply(this, arguments);
        this.load();
    };

    // ---------- generation ----------

    View.prototype.setGenerating = function (generating) {
        this._generating = generating;
        var view = this.view;
        view.querySelector('.btnGenerate').disabled = generating;
        view.querySelector('.btnGenerateLabel').textContent = generating ? 'Generating…' : 'Generate Playlist';
        if (!generating && this._pollTimer) {
            clearTimeout(this._pollTimer);
            this._pollTimer = null;
        }
    };

    View.prototype.showPanel = function (which, html) {
        var view = this.view;
        view.querySelector('.cdResult').style.display = which === 'result' ? '' : 'none';
        view.querySelector('.cdError').style.display = which === 'error' ? '' : 'none';
        view.querySelector(which === 'result' ? '.cdResult' : '.cdError').innerHTML = html;
    };

    View.prototype.renderStages = function (activeStage) {
        var view = this.view;
        var list = view.querySelector('.cdStages');
        var activeIdx = STAGE_ORDER.indexOf(activeStage);
        if (activeStage === 'Completed') {
            activeIdx = STAGE_ORDER.length;
        }

        var html = '';
        for (var i = 0; i < STAGE_ORDER.length; i++) {
            var label = STAGE_LABELS[STAGE_ORDER[i]];
            if (activeStage === 'Completed' || i < activeIdx) {
                html += '<li style="padding:.2em 0;"><span style="color:#6dbb6d;">\u2714</span> ' + label + '</li>';
            } else if (i === activeIdx) {
                html += '<li style="padding:.2em 0;">' +
                    '<span style="display:inline-block;width:.85em;height:.85em;border:2px solid rgba(255,255,255,.25);' +
                    'border-top-color:#fff;border-radius:50%;animation:cdSpin .8s linear infinite;"></span> <b>' + label + '</b></li>';
            } else {
                html += '<li style="padding:.2em 0;opacity:.4;"><span style="opacity:.5;">\u25CB</span> ' + label + '</li>';
            }
        }
        list.innerHTML = html +
            '<style>@keyframes cdSpin{to{transform:rotate(360deg);}}</style>';
    };

    View.prototype.onGenerate = function () {
        var instance = this;
        var view = this.view;
        var prompt = (view.querySelector('.cdPrompt').value || '').trim();

        if (!prompt) {
            this.showPanel('error', 'Describe your playlist theme first.');
            return;
        }

        this.setGenerating(true);
        loading.show();

        // NOTE: form-encode the body — Emby's service layer binds
        // application/x-www-form-urlencoded reliably (plain JSON bodies
        // do not bind on this server build).
        // dataType:'json' is CRITICAL: without it ApiClient.fetch resolves with
        // the raw Response object (verified in modules/emby-apiclient/apiclient.js)
        // for application/json responses — resp.JobId would be undefined.
        ApiClient.ajax({
            type: 'POST',
            url: ApiClient.getUrl('CrateDigger/Create'),
            data: 'Prompt=' + encodeURIComponent(prompt),
            contentType: 'application/x-www-form-urlencoded; charset=UTF-8',
            dataType: 'json'
        }).then(function (resp) {
            loading.hide();
            log('Create response type:', typeof resp, 'value:', resp);
            var data = toJson(resp);
            if (!data || !data.JobId) {
                instance.setGenerating(false);
                instance.showPanel('error', 'The server returned an unexpected response. Check the Emby log for CrateDigger entries.');
                return;
            }
            log('jobId:', data.JobId, '- starting status poll');
            instance.startPolling(data.JobId);
        }, function (err) {
            loading.hide();
            log('Create FAILED:', err);
            instance.setGenerating(false);
            instance.showPanel('error', 'Request failed: ' + errText(err));
        });
    };

    View.prototype.startPolling = function (jobId) {
        var instance = this;
        var view = this.view;

        if (!jobId) {
            this.setGenerating(false);
            this.showPanel('error', 'Missing job id — cannot track progress.');
            return;
        }

        view.querySelector('.cdStatus').style.display = '';
        view.querySelector('.cdResult').style.display = 'none';
        view.querySelector('.cdError').style.display = 'none';
        view.querySelector('.cdResult').innerHTML = '';
        view.querySelector('.cdError').innerHTML = '';
        this.renderStages('Queued');
        view.querySelector('.cdStatusMessage').textContent = 'Queued…';

        function fail(message) {
            instance.showPanel('error', escapeHtml(message));
            instance.setGenerating(false);
        }

        function poll() {
            try {
                var url = ApiClient.getUrl('CrateDigger/Status', { jobId: jobId });
                log('poll:', url);
                // getJSON already sets dataType:'json'; the manual fallback must too
                // (otherwise fetch resolves with the raw Response object).
                var request = ApiClient.getJSON
                    ? ApiClient.getJSON(url)
                    : ApiClient.ajax({ type: 'GET', url: url, dataType: 'json' });

                request.then(function (raw) {
                    try {
                        var status = toJson(raw);
                        log('poll response:', status);
                        if (!status || typeof status !== 'object') {
                            fail('The server returned an unexpected status response.');
                            return;
                        }

                        // Emby's JSON serializer emits PascalCase properties.
                        view.querySelector('.cdStatusMessage').textContent = status.Message || '';
                        instance.renderStages(status.Stage || 'Queued');

                        if (status.Done) {
                            if (status.Success) {
                                var html = '<div style="font-size:1.1em;margin-bottom:.5em;">' +
                                    '<span style="color:#6dbb6d;">\u2714</span> Created playlist <b>' +
                                    escapeHtml(status.PlaylistName || '') + '</b></div>' +
                                    '<div>' + status.Matched + ' tracks matched' +
                                    (status.Unmatched ? ', ' + status.Unmatched + ' unmatched' : '') + '</div>';

                                if (status.UnmatchedTracks && status.UnmatchedTracks.length) {
                                    html += '<details style="margin-top:.5em;"><summary>Unmatched suggestions</summary><ul>';
                                    for (var i = 0; i < status.UnmatchedTracks.length; i++) {
                                        html += '<li>' + escapeHtml(status.UnmatchedTracks[i]) + '</li>';
                                    }
                                    html += '</ul></details>';
                                }
                                instance.showPanel('result', html);
                                sendToast('Playlist created');
                            } else {
                                fail(status.Error || 'Generation failed.');
                                return;
                            }
                            instance.setGenerating(false);
                            return;
                        }

                        instance._pollTimer = setTimeout(poll, 800);
                    } catch (err) {
                        fail('Status handling error: ' + (err && err.message ? err.message : err));
                    }
                }, function (err) {
                    log('poll FAILED:', err);
                    fail('Status check failed: ' + (err && err.message ? err.message : err));
                });
            } catch (err) {
                log('poll EXCEPTION:', err);
                fail('Status poll error: ' + (err && err.message ? err.message : err));
            }
        }

        poll();
    };

    View.prototype.onDestroy = function () {
        if (this._pollTimer) {
            clearTimeout(this._pollTimer);
            this._pollTimer = null;
        }
        BaseView.prototype.onDestroy && BaseView.prototype.onDestroy.apply(this, arguments);
    };

    return View;
});