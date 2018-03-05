dotnet build
dotnet publish
docker build -f "c:\Develop\git\IoTEdgeAllInOne\Docker\arm\Dockerfile" --build-arg EXE_DIR="./bin/Debug/netcoreapp2.0/publish" -t "timatest.azurecr.io/v2/iotedgeallinone:latest" "c:\Develop\git\IoTEdgeAllInOne"
docker push timatest.azurecr.io/v2/iotedgeallinone:latest
