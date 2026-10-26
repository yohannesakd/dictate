FROM mcr.microsoft.com/dotnet/sdk:10.0-noble
RUN apt-get update -qq && apt-get install -y -qq \
    xvfb dbus-x11 at-spi2-core python3-gi gir1.2-gtk-3.0 openbox \
    xdotool imagemagick libfontconfig1 libsecret-1-0 alsa-utils \
    libnss3 libgbm1 libasound2t64 libxss1 libxkbcommon0 \
    && rm -rf /var/lib/apt/lists/*
RUN useradd --create-home --non-unique --uid 1000 tester
