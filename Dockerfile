FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["DigitalScrumBoard1/DigitalScrumBoard1.csproj", "DigitalScrumBoard1/"]
RUN dotnet restore "DigitalScrumBoard1/DigitalScrumBoard1.csproj"
COPY . .
RUN dotnet build "DigitalScrumBoard1/DigitalScrumBoard1.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "DigitalScrumBoard1/DigitalScrumBoard1.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DigitalScrumBoard1.dll"]
