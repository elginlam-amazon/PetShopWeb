FROM mcr.microsoft.com/dotnet/sdk:10.0 as build-env
WORKDIR /app
COPY PetShopWeb/*.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build-env /app/out .
# .NET 8+ images listen on 8080 by default; keep port 80 to match docker-compose mappings.
ENV ASPNETCORE_HTTP_PORTS=80
EXPOSE 80
EXPOSE 433
ENTRYPOINT ["dotnet", "PetShopWeb.dll"]
