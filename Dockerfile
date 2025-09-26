# Use the official .NET SDK image to build the app
FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /source

# Copy the solution and project files first to leverage Docker caching
COPY *.sln .
COPY SignalTracker/*.csproj ./SignalTracker/

# Restore dependencies
RUN dotnet restore "SignalTracker/SignalTracker.csproj"

# Copy the rest of the source code
COPY SignalTracker/. ./SignalTracker/

# Publish the application
WORKDIR /source/SignalTracker
RUN dotnet publish "SignalTracker.csproj" -c Release -o /app/publish

# Use the official ASP.NET Core runtime image for the final, smaller image
FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "SignalTracker.dll"]