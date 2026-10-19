# Stage 1 - Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore PetShopWeb/PetShopWeb.csproj
RUN dotnet publish PetShopWeb/PetShopWeb.csproj -c Release -o /app/publish

# Stage 2 - Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "PetShopWeb.dll"]
