param([Parameter(Mandatory = $true)][string]$BackupPath)
$ErrorActionPreference = "Stop"
$backup = (Resolve-Path $BackupPath).Path
$manifest = Get-Content (Join-Path $backup "manifest.json") -Encoding utf8 | ConvertFrom-Json
foreach ($file in $manifest.files) {
    $actual = (Get-FileHash -Algorithm SHA256 (Join-Path $backup $file.path)).Hash.ToLowerInvariant()
    if ($actual -ne $file.sha256) { throw "Backup hash mismatch: $($file.path)" }
}
$suffix = [Guid]::NewGuid().ToString("N").Substring(0, 10)
$pg = "dealeros-restore-pg-$suffix"; $minio = "dealeros-restore-minio-$suffix"
$restoreVolume = "dealeros-restore-files-$suffix"
try {
    docker volume create $restoreVolume | Out-Null
    docker run --rm -v "${backup}:/input" -v "${restoreVolume}:/restore" busybox:1.37 tar -xf /input/minio.tar -C /restore
    if ($LASTEXITCODE -ne 0) { throw "MinIO archive extraction failed." }
    docker run -d --name $pg --tmpfs /var/lib/postgresql/data -e POSTGRES_DB=dealeros_restore -e POSTGRES_USER=restore -e POSTGRES_PASSWORD=restore postgres:17-alpine | Out-Null
    docker run -d --name $minio --tmpfs /data -v "${restoreVolume}:/restore-source" -e MINIO_ROOT_USER=restore-test -e MINIO_ROOT_PASSWORD=restore-test-secret minio/minio:RELEASE.2025-09-07T16-13-09Z server /data | Out-Null
    for ($i = 0; $i -lt 40; $i++) { docker exec $pg pg_isready -U restore -d dealeros_restore *> $null; if ($LASTEXITCODE -eq 0) { break }; Start-Sleep -Seconds 1 }
    if ($LASTEXITCODE -ne 0) { throw "Temporary PostgreSQL did not become ready." }
    docker cp (Join-Path $backup "postgres.dump") "${pg}:/tmp/postgres.dump"
    docker exec $pg pg_restore -U restore -d dealeros_restore --no-owner --no-privileges /tmp/postgres.dump
    if ($LASTEXITCODE -ne 0) { throw "PostgreSQL restore failed." }
    $migrationCount = (@('SELECT COUNT(*) FROM "__EFMigrationsHistory";') | docker exec -i $pg psql -U restore -d dealeros_restore -At).Trim()
    $vehicleCount = (@('SELECT COUNT(*) FROM vehicles.vehicles;') | docker exec -i $pg psql -U restore -d dealeros_restore -At).Trim()
    $dealCount = (@('SELECT COUNT(*) FROM deals.deals;') | docker exec -i $pg psql -U restore -d dealeros_restore -At).Trim()
    $profitCount = (@('SELECT COUNT(*) FROM finance.profit_snapshots;') | docker exec -i $pg psql -U restore -d dealeros_restore -At).Trim()
    if ([int]$migrationCount -lt 1 -or [int]$vehicleCount -lt 1) { throw "Restored PostgreSQL smoke checks failed." }

    $objectManifest = (Get-Content (Join-Path $backup "private-object.sha256") -Encoding utf8).Trim()
    if ($objectManifest -notmatch '^([0-9a-f]{64})\s+(.+)$') { throw "Private object manifest is invalid." }
    $expectedObjectHash = $Matches[1]; $relative = $Matches[2].TrimStart('.', '/')
    for ($i = 0; $i -lt 40; $i++) { docker exec $minio mc ready local *> $null; if ($LASTEXITCODE -eq 0) { break }; Start-Sleep -Seconds 1 }
    docker exec $minio sh -c "mc alias set restore-local http://localhost:9000 restore-test restore-test-secret >/dev/null && mc mb --ignore-existing restore-local/dealeros-private >/dev/null && mc mirror --overwrite /restore-source restore-local/dealeros-private >/dev/null"
    if ($LASTEXITCODE -ne 0) { throw "MinIO restore failed." }
    docker exec $minio mc cp "restore-local/dealeros-private/$relative" /restore-source/.verified-object *> $null
    $restoredHash = (docker run --rm -v "${restoreVolume}:/restore" busybox:1.37 sha256sum /restore/.verified-object).Split(' ')[0].Trim()
    if ($restoredHash -ne $expectedObjectHash) { throw "Restored private object hash mismatch." }
    [ordered]@{ migrations = [int]$migrationCount; vehicles = [int]$vehicleCount; deals = [int]$dealCount; profitSnapshots = [int]$profitCount; privateObject = $relative; privateObjectSha256 = $restoredHash } | ConvertTo-Json
}
finally {
    docker rm -f $pg $minio *> $null
    docker volume rm $restoreVolume *> $null
}
