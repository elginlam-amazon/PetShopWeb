FROM mcr.microsoft.com/dotnet/sdk:10.0 as build-env
WORKDIR /src
COPY PetShopWeb/PetShopWeb.csproj PetShopWeb/
RUN dotnet restore PetShopWeb/PetShopWeb.csproj
COPY . .
RUN dotnet publish PetShopWeb/PetShopWeb.csproj -c Release -o /app/out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build-env /app/out .
# ASP.NET Core 8+ container images default to port 8080; keep serving on 80
# so the existing docker-compose port mapping (3000:80) stays valid.
ENV ASPNETCORE_HTTP_PORTS=80
EXPOSE 80
ENTRYPOINT ["dotnet", "PetShopWeb.dll"]
