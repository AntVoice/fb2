#!/bin/bash

set -e -o pipefail

dotnet new tool-manifest
dotnet tool install fake-cli
