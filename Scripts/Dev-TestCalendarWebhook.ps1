# Posts a fake Google Calendar push to the local Functions host and prints
# what to look for in the console. Does not require a real Google channel:
# an unknown ChannelId still proves enqueue + queue trigger (then skip).
#
# Prerequisites: Dev-StartFunctionHost / Dev-StartDebugSession (func on 7074,
# Azurite running).
#
# Usage:
#   .\Scripts\Dev-TestCalendarWebhook.ps1
#   .\Scripts\Dev-TestCalendarWebhook.ps1 -ChannelId "<GoogleChannelId from UserSettings>"

param(
    [string] $BaseUrl = "https://localhost:7074",
    [string] $ChannelId = "local-test-channel",
    [string] $ChannelToken = "local-test-token",
    [ValidateSet("exists", "sync")]
    [string] $ResourceState = "exists"
)

$ErrorActionPreference = "Stop"
$url = "$BaseUrl/api/googlecalendar/webhook"
$headers = @{
    "X-Goog-Channel-Id"     = $ChannelId
    "X-Goog-Channel-Token"  = $ChannelToken
    "X-Goog-Resource-State" = $ResourceState
}

Write-Host "POST $url"
Write-Host "  X-Goog-Channel-Id     = $ChannelId"
Write-Host "  X-Goog-Resource-State = $ResourceState"

try {
    $invoke = @{
        Uri             = $url
        Method          = "POST"
        Headers         = $headers
        UseBasicParsing = $true
    }
    if ($PSVersionTable.PSVersion.Major -ge 6) {
        $invoke.SkipCertificateCheck = $true
    }
    $response = Invoke-WebRequest @invoke
    Write-Host "HTTP $($response.StatusCode) (expect 200)"
} catch {
    Write-Host "HTTP request failed: $($_.Exception.Message)"
    if ($_.Exception.Response) {
        Write-Host "Status: $([int]$_.Exception.Response.StatusCode)"
    }
    throw
}

Write-Host ""
Write-Host "In the Functions console look for EventIds:"
if ($ResourceState -eq "sync") {
    Write-Host "  3102 WebhookHandshake  (not enqueued)"
} else {
    Write-Host "  3101 WebhookReceived"
    Write-Host "  3103 Enqueued"
    Write-Host "  3109 ImportStarted     OR 3106 UnknownChannel (expected for local-test-channel)"
    Write-Host "  3110 ImportFinished    if ChannelId matches a connected user"
    Write-Host "  3104 EnqueueFailed     if Azurite is not running"
    Write-Host "  3113 ApproachingPoison if the same message keeps failing"
}
Write-Host ""
Write-Host "App Insights (production) examples:"
Write-Host '  traces | where customDimensions.EventId in ("3104","3105","3106","3107","3112","3113")'
Write-Host '  traces | where message startswith "Google calendar"'
