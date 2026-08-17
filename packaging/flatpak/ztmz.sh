#!/bin/sh

export ZTMZ_RESOURCE_HOME="${ZTMZ_RESOURCE_HOME:-/app/share/ztmz}"

exec /app/lib/dotnet/dotnet /app/lib/ztmz/ztmz.dll "$@"
