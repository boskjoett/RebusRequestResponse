FROM mcr.microsoft.com/dotnet/core/runtime:3.1

WORKDIR /app

# Copy binaries from local relative path to WORKDIR in image
COPY bin/Release/netcoreapp3.1/publish/ ./

ENTRYPOINT ["dotnet", "Responder.dll"]
