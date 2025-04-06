#!/bin/bash
set -e

# Color codes for output
GREEN='\033[0;32m'
YELLOW='\033[0;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

echo -e "${GREEN}Pressure Sensor System - Local Development Setup${NC}"
echo "=========================="

# Create directory structure
mkdir -p Publisher Subscriber PressureSensorProducer
mkdir -p infra/grafana/provisioning/datasources infra/grafana/provisioning/dashboards infra/grafana/dashboards

# Copy files from the provided source
echo -e "${YELLOW}Copying source files...${NC}"

# Copy Publisher files
cp Publisher.csproj Publisher/
cp Program.cs Publisher/  # The one from document index 9

# Copy Subscriber files
cp Subscriber.csproj Subscriber/
cp Program.cs Subscriber/  # The one from document index 11

# Copy PressureSensorProducer files
cp PressureSensorProducer.csproj PressureSensorProducer/
cp Program.cs PressureSensorProducer/  # The one from document index 6
cp TcpListenerService.cs PressureSensorProducer/
cp appsettings.json PressureSensorProducer/
cp appsettings.Development.json PressureSensorProducer/

# Create Docker files
echo -e "${YELLOW}Creating Docker files...${NC}"

# Create Publisher Dockerfile
cat > Publisher/Dockerfile << 'EOF'
FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY ["Publisher.csproj", "./"]
RUN dotnet restore

# Copy everything else and build
COPY . .
RUN dotnet publish -c Release -o /app/publish

# Build runtime image
FROM mcr.microsoft.com/dotnet/runtime:7.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Publisher.dll"]
EOF

# Create Subscriber Dockerfile
cat > Subscriber/Dockerfile << 'EOF'
FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY ["Subscriber.csproj", "./"]
RUN dotnet restore

# Copy everything else and build
COPY . .
RUN dotnet publish -c Release -o /app/publish

# Build runtime image
FROM mcr.microsoft.com/dotnet/runtime:7.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Subscriber.dll"]
EOF

# Create PressureSensorProducer Dockerfile
cat > PressureSensorProducer/Dockerfile << 'EOF'
FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY ["PressureSensorProducer.csproj", "./"]
RUN dotnet restore

# Copy everything else and build
COPY . .
RUN dotnet publish -c Release -o /app/publish

# Build runtime image
FROM mcr.microsoft.com/dotnet/runtime:7.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "PressureSensorProducer.dll"]
EOF

# Create Grafana configuration files
echo -e "${YELLOW}Creating Grafana configuration...${NC}"

# Create Grafana datasource configuration
cat > infra/grafana/provisioning/datasources/influxdb.yml << 'EOF'
apiVersion: 1

datasources:
  - name: InfluxDB
    type: influxdb
    access: proxy
    url: http://influxdb:8086
    jsonData:
      version: Flux
      organization: my-org
      defaultBucket: pressure-data
      tlsSkipVerify: true
    secureJsonData:
      token: my-token
EOF

# Create Grafana dashboard provider configuration
cat > infra/grafana/provisioning/dashboards/default.yml << 'EOF'
apiVersion: 1

providers:
  - name: 'Default'
    orgId: 1
    folder: ''
    type: file
    disableDeletion: false
    updateIntervalSeconds: 10
    allowUiUpdates: true
    options:
      path: /var/lib/grafana/dashboards
      foldersFromFilesStructure: true
EOF

# Create a sample dashboard JSON
cat > infra/grafana/dashboards/pressure_dashboard.json << 'EOF'
{
  "annotations": {
    "list": [
      {
        "builtIn": 1,
        "datasource": "-- Grafana --",
        "enable": true,
        "hide": true,
        "iconColor": "rgba(0, 211, 255, 1)",
        "name": "Annotations & Alerts",
        "target": {
          "limit": 100,
          "matchAny": false,
          "tags": [],
          "type": "dashboard"
        },
        "type": "dashboard"
      }
    ]
  },
  "editable": true,
  "fiscalYearStartMonth": 0,
  "graphTooltip": 0,
  "id": null,
  "links": [],
  "panels": [
    {
      "fieldConfig": {
        "defaults": {
          "color": {
            "mode": "palette-classic"
          },
          "custom": {
            "axisLabel": "",
            "axisPlacement": "auto",
            "barAlignment": 0,
            "drawStyle": "line",
            "fillOpacity": 10,
            "gradientMode": "none",
            "hideFrom": {
              "legend": false,
              "tooltip": false,
              "viz": false
            },
            "lineInterpolation": "linear",
            "lineWidth": 1,
            "pointSize": 5,
            "scaleDistribution": {
              "type": "linear"
            },
            "showPoints": "auto",
            "spanNulls": false,
            "stacking": {
              "group": "A",
              "mode": "none"
            },
            "thresholdsStyle": {
              "mode": "off"
            }
          },
          "mappings": [],
          "thresholds": {
            "mode": "absolute",
            "steps": [
              {
                "color": "green",
                "value": null
              },
              {
                "color": "red",
                "value": 80
              }
            ]
          },
          "unit": "pressurehpa"
        },
        "overrides": []
      },
      "gridPos": {
        "h": 9,
        "w": 24,
        "x": 0,
        "y": 0
      },
      "id": 2,
      "options": {
        "legend": {
          "calcs": ["mean", "max", "min"],
          "displayMode": "table",
          "placement": "bottom",
          "showLegend": true
        },
        "tooltip": {
          "mode": "single",
          "sort": "none"
        }
      },
      "title": "Pressure Sensor Readings",
      "type": "timeseries"
    }
  ],
  "refresh": "5s",
  "schemaVersion": 36,
  "style": "dark",
  "tags": ["pressure", "sensors"],
  "templating": {
    "list": []
  },
  "time": {
    "from": "now-6h",
    "to": "now"
  },
  "timepicker": {},
  "timezone": "",
  "title": "Pressure Sensor Dashboard",
  "version": 1,
  "uid": "pressure-sensor-dashboard"
}
EOF

echo -e "${GREEN}Setup complete! You can now start the system with:${NC}"
echo "docker-compose up -d"
echo ""
echo "Access Grafana at: http://localhost:3000"
echo "Username: admin"
echo "Password: admin"
echo ""
echo "Your InfluxDB token is: my-token"
echo "You can find this token in the docker-compose.yml file as DOCKER_INFLUXDB_INIT_ADMIN_TOKEN"