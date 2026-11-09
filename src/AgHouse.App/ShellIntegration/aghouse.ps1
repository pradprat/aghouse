# aghouse shell integration for PowerShell 7+ and Windows PowerShell 5.1.
# Dot-sourced by aghouse after your $PROFILE. Reports the working directory (OSC 7)
# and prompt boundaries (OSC 133) so the sidebar can show cwd and git branch.

if (-not $env:AGHOUSE_PANE_ID -or $global:__aghouseIntegrated) { return }
$global:__aghouseIntegrated = $true

$global:__aghouseOriginalPrompt = $function:prompt

function global:prompt {
    $esc = [char]27
    $bel = [char]7
    $prefix = ''
    $loc = $executionContext.SessionState.Path.CurrentLocation
    if ($loc.Provider.Name -eq 'FileSystem') {
        $uri = ([System.Uri]::new($loc.ProviderPath)).AbsoluteUri   # file:///C:/x%20y
        $prefix = "$esc]7;$uri$bel"
    }
    $original = if ($global:__aghouseOriginalPrompt) { & $global:__aghouseOriginalPrompt } else { "PS $loc> " }
    return "$prefix$esc]133;A$bel$original$esc]133;B$bel"
}
