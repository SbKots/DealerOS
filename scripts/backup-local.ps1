param([string]$OutputRoot = ".local-backups")
$ErrorActionPreference = "Stop"
$root = (Resolve-Path ".").Path
$target = Join-Path $root (Join-Path $OutputRoot (Get-Date -Format "yyyyMMdd-HHmmss"))
New-Item -ItemType Directory -Force -Path $target | Out-Null
$postgres = (docker compose ps -q postgres).Trim()
$minio = (docker compose ps -q minio).Trim()
if (-not $postgres -or -not $minio) { throw "PostgreSQL and MinIO compose services must be running." }

docker compose exec -T postgres pg_dump -U dealeros -d dealeros -Fc -f /tmp/dealeros-local.dump
if ($LASTEXITCODE -ne 0) { throw "pg_dump failed." }
docker cp "${postgres}:/tmp/dealeros-local.dump" (Join-Path $target "postgres.dump")
docker compose exec -T postgres rm -f /tmp/dealeros-local.dump

$backupVolume = "dealeros-backup-$([Guid]::NewGuid().ToString('N').Substring(0, 10))"
docker volume create $backupVolume | Out-Null
try {
    docker run --rm --network dealeros_default -v "${backupVolume}:/backup" --entrypoint /bin/sh minio/mc:RELEASE.2025-08-13T08-35-41Z -c 'mc alias set source http://minio:9000 dealer-test dealer-test-secret >/dev/null && mc mirror --overwrite source/dealeros-private /backup >/dev/null'
    if ($LASTEXITCODE -ne 0) { throw "MinIO copy failed." }
    docker run --rm -v "${backupVolume}:/backup" -v "${target}:/out" busybox:1.37 sh -c 'file=$(find /backup -type f | head -n 1) && test -n "$file" && sha256sum "$file" | sed "s#/backup/##" > /out/private-object.sha256 && tar -cf /out/minio.tar -C /backup .'
    if ($LASTEXITCODE -ne 0) { throw "MinIO archive failed." }
}
finally { docker volume rm $backupVolume *> $null }

$files = Get-ChildItem -File -Recurse $target | ForEach-Object {
    [ordered]@{ path = $_.FullName.Substring($target.Length + 1).Replace('\', '/'); length = $_.Length; sha256 = (Get-FileHash -Algorithm SHA256 $_.FullName).Hash.ToLowerInvariant() }
}
$manifest = [ordered]@{ format = "DealerOS.LocalBackup.v1"; createdAt = (Get-Date).ToUniversalTime().ToString("o"); files = @($files) }
$manifest | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 (Join-Path $target "manifest.json")
Write-Output $target
