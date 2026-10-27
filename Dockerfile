# Imagem base de compilação SDK .NET 10
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

# Copiar csproj e restaurar dependências
COPY src/MglConnect.Web/*.csproj src/MglConnect.Web/
RUN dotnet restore src/MglConnect.Web/MglConnect.Web.csproj

# Copiar código fonte completo e publicar
COPY src/MglConnect.Web/ src/MglConnect.Web/
WORKDIR /app/src/MglConnect.Web
RUN dotnet publish -c Release -o /app/publish

# Imagem runtime otimizada para produção
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 80
EXPOSE 443

ENV ASPNETCORE_URLS=http://+:80
ENV PORT=80
ENTRYPOINT ["dotnet", "MglConnect.Web.dll"]
