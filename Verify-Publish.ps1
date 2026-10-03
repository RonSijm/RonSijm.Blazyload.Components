param(
    [string] $BrowserChannel = "msedge",
    [switch] $GitHubPages,
    [ValidateSet("All", "Orchestrator", "Simple", "Extensive")]
    [string] $Demo = "Orchestrator"
)

$ErrorActionPreference = "Stop"
$previousPublishRoot = $env:BLAZY_COMPONENTS_PUBLISH_ROOT
$previousBrowserChannel = $env:BLAZY_COMPONENTS_BROWSER_CHANNEL
$previousBasePath = $env:BLAZY_COMPONENTS_BASE_PATH
$previousDemo = $env:BLAZY_COMPONENTS_DEMO
Push-Location $PSScriptRoot
try {
    $demos = @("Orchestrator", "Simple", "Extensive")
    if ($Demo -ne "All") {
        $demos = $demos | Where-Object { $_ -eq $Demo }
    }
    foreach ($selectedDemo in $demos) {
        if ($selectedDemo -eq "Orchestrator") {
            $hostName = "RonSijm.Demo.Blazyload.Components.Orchestrator"
            $projectDirectory = Join-Path $PSScriptRoot "Examples\Orchestrator"
        }
        else {
            $hostName = "RonSijm.Demo.Blazyload.Components.$selectedDemo.Host"
            $projectDirectory = Join-Path $PSScriptRoot "Examples\$selectedDemo\$hostName"
        }
        dotnet publish (Join-Path $projectDirectory "$hostName.csproj") -c Release "-p:GHPages=$($GitHubPages.IsPresent)" --nologo --verbosity quiet -nr:false
        if ($LASTEXITCODE -ne 0) {
            throw "$selectedDemo demo publication failed."
        }

        $env:BLAZY_COMPONENTS_PUBLISH_ROOT = Join-Path $projectDirectory "bin\Release\net10.0\publish\wwwroot"
        $env:BLAZY_COMPONENTS_BROWSER_CHANNEL = $BrowserChannel
        $env:BLAZY_COMPONENTS_DEMO = $selectedDemo
        $env:BLAZY_COMPONENTS_BASE_PATH = "/"
        if ($GitHubPages) {
            $env:BLAZY_COMPONENTS_BASE_PATH = "/RonSijm.Blazyload.Components/"
            if ($selectedDemo -ne "Orchestrator") {
                $env:BLAZY_COMPONENTS_BASE_PATH += "$selectedDemo/"
            }
        }

        dotnet test .\Tests\RonSijm.Blazyload.Components.IntegrationTests\RonSijm.Blazyload.Components.IntegrationTests.csproj --nologo --verbosity quiet --logger "console;verbosity=normal" -nr:false
        if ($LASTEXITCODE -ne 0) {
            throw "$selectedDemo published demo verification failed."
        }
    }
}
finally {
    $env:BLAZY_COMPONENTS_PUBLISH_ROOT = $previousPublishRoot
    $env:BLAZY_COMPONENTS_BROWSER_CHANNEL = $previousBrowserChannel
    $env:BLAZY_COMPONENTS_BASE_PATH = $previousBasePath
    $env:BLAZY_COMPONENTS_DEMO = $previousDemo
    Pop-Location
}
