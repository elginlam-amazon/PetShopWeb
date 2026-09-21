FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build-env
WORKDIR /src
COPY PetShopWeb/PetShopWeb.csproj PetShopWeb/
RUN dotnet restore PetShopWeb/PetShopWeb.csproj
COPY . .
RUN dotnet publish PetShopWeb/PetShopWeb.csproj -c Release -o /app/out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build-env /app/out .
# The .NET 8+ runtime images default to port 8080; keep 80 so the existing
# docker-compose port mapping continues to work.
ENV ASPNETCORE_HTTP_PORTS=80
EXPOSE 80
EXPOSE 433
ENTRYPOINT ["dotnet", "PetShopWeb.dll"]
