#!/bin/bash

set -e -o pipefail

dotnet new tool-manifest --force
dotnet tool install fake-cli
