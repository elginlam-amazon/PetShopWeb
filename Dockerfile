# Stage 1 - Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build-env
WORKDIR /src
COPY PetShopWeb/PetShopWeb.csproj PetShopWeb/
RUN dotnet restore PetShopWeb/PetShopWeb.csproj
COPY . .
RUN dotnet publish PetShopWeb/PetShopWeb.csproj -c Release -o /app/out

# Stage 2 - Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build-env /app/out .
# The aspnet images default to port 8080; keep port 80 so the existing
# docker-compose port mappings continue to work.
ENV ASPNETCORE_HTTP_PORTS=80
EXPOSE 80
ENTRYPOINT ["dotnet", "PetShopWeb.dll"]
