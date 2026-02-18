# syntax=docker/dockerfile:1

# ------------------------------------------------------------
# Build stage
# ------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution + csproj first for better layer caching
COPY ./stripe-webhooks-dotnet.sln ./
COPY ./src/StripeWebhooks.Api/StripeWebhooks.Api.csproj ./src/StripeWebhooks.Api/
COPY ./tests/StripeWebhooks.Tests/StripeWebhooks.Tests.csproj ./tests/StripeWebhooks.Tests/

RUN dotnet restore ./stripe-webhooks-dotnet.sln

# Copy the rest and publish
COPY . .

RUN dotnet publish ./src/StripeWebhooks.Api/StripeWebhooks.Api.csproj \
    -c Release \
    -o /out \
    /p:UseAppHost=false

# ------------------------------------------------------------
# Runtime stage
# ------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Install curl for Docker healthchecks (Debian-based image)
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Create a non-root user and switch to it
RUN useradd --create-home --shell /usr/sbin/nologin appuser
USER appuser

COPY --from=build /out ./

EXPOSE 7020
ENV ASPNETCORE_URLS=http://0.0.0.0:7020

ENTRYPOINT ["dotnet", "StripeWebhooks.Api.dll"]
