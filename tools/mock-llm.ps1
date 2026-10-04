# Mock OpenAI-compatible chat completions server for CrateDigger ST-002 testing.
# Answers the artist-shortlist prompt and the tracklist prompt distinctly.
$log = "$env:TEMP\mock-llm.log"

$artistJson = '{"artists":["ABBA","Cheap Trick","Billy Paul","Blue Oyster Cult","The Smithereens"]}'

$trackJson = @'
```json
{"name":"CrateDigger Mock Mix","tracks":[
 {"artist":"ABBA","title":"Gimme! Gimme! Gimme! (A Man After Midnight)"},
 {"artist":"Cheap Trick","title":"Surrender"},
 {"artist":"Billy Paul","title":"Me and Mrs. Jones"},
 {"artist":"Blue Oyster Cult","title":"Don't Fear The Reaper"},
 {"artist":"The Smithereens","title":"A Girl Like You"}
]}
```
'@

$artistContent = $artistJson

$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add('http://localhost:12345/')
try {
    $listener.Start()
} catch {
    "FAILED to start listener: $_" | Out-File $log
    exit 1
}
"mock-llm started $(Get-Date -Format o)" | Out-File $log

while ($listener.IsListening) {
    try {
        $ctx = $listener.GetContext()
        $reader = New-Object System.IO.StreamReader($ctx.Request.InputStream, [Text.Encoding]::UTF8)
        $body = $reader.ReadToEnd()
        $reader.Close()

        $marker = $ctx.Request.Url.AbsolutePath + ' ' + $body.Substring(0, [Math]::Min(300, $body.Length))
        ("REQ " + (Get-Date -Format 'HH:mm:ss') + ' ' + $marker) | Add-Content $log

        if ($body -match 'Pick the artists most relevant') {
            $content = $artistContent
            $which = 'artists'
        } else {
            $content = $trackJson
            $which = 'tracks'
        }
        ("  -> responding with " + $which) | Add-Content $log

        $payload = [pscustomobject]@{
            id = 'mock-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
            object = 'chat.completion'
            model = 'mock'
            choices = @(
                [pscustomobject]@{
                    index = 0
                    finish_reason = 'stop'
                    message = [pscustomobject]@{ role = 'assistant'; content = $content }
                }
            )
        } | ConvertTo-Json -Depth 8

        $bytes = [Text.Encoding]::UTF8.GetBytes($payload)
        $ctx.Response.StatusCode = 200
        $ctx.Response.ContentType = 'application/json; charset=utf-8'
        $ctx.Response.ContentLength64 = $bytes.Length
        $ctx.Response.OutputStream.Write($bytes, 0, $bytes.Length)
        $ctx.Response.OutputStream.Close()
    } catch {
        ("ERR " + $_.Exception.Message) | Add-Content $log
    }
}