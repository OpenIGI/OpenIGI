# Rebuilds apps/OpenIGI.Web/wwwroot/discord-sdk.js: Discord's Embedded App SDK and its dependencies bundled into one ES module,
# since a page inside a Discord Activity may load scripts from its own origin only. Run from the repository root:
#
#   pwsh tools/web-vendor/discord-sdk.ps1 [-Version 2.5.0]
#
# Packages are installed into a temporary folder with their install scripts off; nothing is added to the repository but the
# bundle.
param([string]$Version = "2.5.0")
$ErrorActionPreference = "Stop"
$work = Join-Path ([IO.Path]::GetTempPath()) ("discord-sdk-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory $work | Out-Null
try {
    Set-Content -Path (Join-Path $work "package.json") -Value '{"name":"bundle","private":true}' -Encoding utf8
    Set-Content -Path (Join-Path $work "entry.mjs") -Value 'export { DiscordSDK, patchUrlMappings } from "@discord/embedded-app-sdk";' -Encoding utf8
    npm install --prefix $work --ignore-scripts --no-audit --no-fund "@discord/embedded-app-sdk@$Version" "esbuild@0.24.0" | Out-Null
    & (Join-Path $work "node_modules/.bin/esbuild") (Join-Path $work "entry.mjs") --bundle --format=esm --minify --target=es2020 "--outfile=$(Join-Path $work 'discord-sdk.js')"
    $header = "// Discord's Embedded App SDK $Version (@discord/embedded-app-sdk, MIT) with its dependencies (zod, eventemitter3, uuid,`n// big-integer, decimal.js-light, lodash.transform; all MIT), bundled into one file: inside a Discord Activity the page may load`n// scripts from its own origin only. Rebuild with tools/web-vendor/discord-sdk.ps1.`n"
    [IO.File]::WriteAllText((Join-Path $PWD "apps/OpenIGI.Web/wwwroot/discord-sdk.js"), $header + [IO.File]::ReadAllText((Join-Path $work "discord-sdk.js")))
}
finally {
    Remove-Item -Recurse -Force $work
}
