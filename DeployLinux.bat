dotnet build
dotnet publish
docker build -f "c:\Develop\git\IoTEdgeAllInOne\Docker\Linux\Dockerfile" --build-arg EXE_DIR="./bin/Debug/netcoreapp2.0/publish" -t "timatest.azurecr.io/v2/iotedgeallinonelinux:latest" "c:\Develop\git\IoTEdgeAllInOne"
docker push timatest.azurecr.io/v2/iotedgeallinonelinux:latest
