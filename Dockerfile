#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443
ENV ASPNETCORE_URLS=http://*:5000

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["RivalCoins.Airdrop/RivalCoins.Airdrop.csproj", "RivalCoins.Airdrop/"]
RUN dotnet restore "RivalCoins.Airdrop/RivalCoins.Airdrop.csproj"
COPY . .
WORKDIR "/src/RivalCoins.Airdrop"
RUN dotnet build "RivalCoins.Airdrop.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "RivalCoins.Airdrop.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "RivalCoins.Airdrop.dll"]