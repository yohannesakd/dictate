FROM mcr.microsoft.com/dotnet/sdk:10.0
RUN apt-get update -qq && apt-get install -y -qq \
    xvfb dbus-x11 at-spi2-core python3-gi gir1.2-gtk-3.0 openbox \
    xdotool imagemagick libfontconfig1 libsecret-1-0 alsa-utils \
    && rm -rf /var/lib/apt/lists/*
