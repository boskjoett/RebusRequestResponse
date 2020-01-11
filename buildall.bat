cd Requester
dotnet publish -c Release
docker build -t rebus-requester .
cd..

cd Responder
dotnet publish -c Release
docker build -t rebus-responder .
cd..
