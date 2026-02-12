# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

RUN apt-get update \
	&& apt-get install -y --no-install-recommends git cmake build-essential \
	&& rm -rf /var/lib/apt/lists/*

# Build StormLib from source for Linux so MPQ archives can be read inside the container
RUN git clone --depth 1 https://github.com/ladislav-zezula/StormLib.git /tmp/stormlib \
	&& cmake -S /tmp/stormlib -B /tmp/stormlib/build -DBUILD_SHARED_LIBS=ON -DCMAKE_BUILD_TYPE=Release \
	&& cmake --build /tmp/stormlib/build --config Release --target storm \
	&& libpath=$(find /tmp/stormlib/build -name "libstorm.so" -print -quit) \
	&& test -n "$libpath" \
	&& cp "$libpath" /usr/local/lib/libstorm.so

COPY . .
RUN if [ ! -f PathingAPI/data_config.json ]; then \
	echo '{"Version":14,"Root":"..\\json"}' > PathingAPI/data_config.json; \
	fi
RUN dotnet restore MasterOfPuppets.sln /p:EnableWindowsTargeting=true
WORKDIR /src/PathingAPI
RUN dotnet publish PathingAPI.csproj -c Release -o /app/publish /p:UseAppHost=false /p:EnableWindowsTargeting=true \
	&& cp /usr/local/lib/libstorm.so /app/publish/

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://0.0.0.0:5001
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
COPY --from=build /app/publish .
EXPOSE 5001
ENTRYPOINT ["dotnet", "PathingAPI.dll"]
