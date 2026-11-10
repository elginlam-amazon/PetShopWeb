FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build-env
WORKDIR /app
COPY PetShopWeb/*.csproj ./PetShopWeb/
RUN dotnet restore PetShopWeb/PetShopWeb.csproj
COPY . .
RUN dotnet publish PetShopWeb/PetShopWeb.csproj -c Release -o out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build-env /app/out .
EXPOSE 80
EXPOSE 443
ENTRYPOINT ["dotnet", "PetShopWeb.dll"]
