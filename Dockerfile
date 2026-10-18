FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build-env
WORKDIR /src
COPY PetShopWeb/PetShopWeb.csproj PetShopWeb/
RUN dotnet restore PetShopWeb/PetShopWeb.csproj
COPY . .
RUN dotnet publish PetShopWeb/PetShopWeb.csproj -c Release -o /app/out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build-env /app/out .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "PetShopWeb.dll"]
