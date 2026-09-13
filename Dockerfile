# Imagem da API. Multi-stage: o SDK (≈ 1 GB) compila, e só o publicado vai para a imagem final, que roda sobre
# o runtime enxuto. Compilar e executar na mesma imagem entregaria compilador, código-fonte e cache de NuGet
# junto com a aplicação — superfície de ataque e centenas de megabytes sem propósito.

# ─────────────────────────────── build ───────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Os arquivos de projeto e os de versão ANTES do código-fonte, de propósito: o Docker guarda o resultado de cada
# camada e só refaz a partir da primeira que mudou. Como o restore depende apenas destes arquivos, editar uma
# classe não o invalida — e o restore é o passo caro.
# O .editorconfig entra junto, e não é opcional: ele carrega a severidade das regras de análise, e o
# Directory.Build.props liga EnforceCodeStyleInBuild com TreatWarningsAsErrors. Sem ele, regras suprimidas no
# repositório voltam a valer dentro do contêiner e o build falha por algo que passa na máquina de quem escreveu —
# foi exatamente o que aconteceu ao construir esta imagem pela primeira vez (CA1716, sobre o tipo `Error`).
COPY Directory.Build.props Directory.Packages.props global.json .editorconfig ./
COPY src/CleanStart.Domain/CleanStart.Domain.csproj src/CleanStart.Domain/
COPY src/CleanStart.Application/CleanStart.Application.csproj src/CleanStart.Application/
COPY src/CleanStart.Infrastructure/CleanStart.Infrastructure.csproj src/CleanStart.Infrastructure/
COPY src/CleanStart.Api/CleanStart.Api.csproj src/CleanStart.Api/

RUN dotnet restore src/CleanStart.Api/CleanStart.Api.csproj

# Só agora o código. A partir daqui toda alteração invalida as camadas seguintes — que são as baratas.
COPY src/ src/

# --no-restore porque o passo acima já resolveu os pacotes; sem isso o restore roda de novo, ignorando o cache.
RUN dotnet publish src/CleanStart.Api/CleanStart.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

# ────────────────────────────── runtime ──────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Usuário sem privilégios. A imagem do .NET já traz o `app` criado; usá-lo é o que impede que uma falha na
# aplicação vire root dentro do contêiner — e, com uma montagem mal configurada, root fora dele.
USER app

# 8080 e não 80: porta abaixo de 1024 exige privilégio para abrir, o que obrigaria a rodar como root. É o padrão
# das imagens .NET desde a 8.
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

COPY --from=build /app/publish .

# Sem HEALTHCHECK aqui, e é decisão: a aplicação expõe /health/live e /health/ready, e quem os consulta é o
# orquestrador — que sabe distinguir "reiniciar o contêiner" de "tirar do balanceador". Um HEALTHCHECK do Docker
# só sabe fazer a primeira coisa. O compose, que não tem orquestrador, define o seu.

ENTRYPOINT ["dotnet", "CleanStart.Api.dll"]
