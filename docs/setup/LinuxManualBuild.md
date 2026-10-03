# Building the Linux AppImage by hand

The release build runs in GitHub Actions and needs no Linux machine, which is what [Publishing](../Publishing.md) describes. These are the same commands run by hand, for when the workflow is unavailable or you are debugging what it does.

## Step 1: Build (on Windows)

.NET cross-compiles, so this produces Linux binaries without needing a Linux machine:

```bash
dotnet publish ArgoBooks.Desktop -c Release -f net10.0 -r linux-x64 --self-contained -o publish/linux-x64
```

## Step 2: Copy to Linux VM

Copy these to your Linux VM (e.g. via shared folder, Google Drive, or USB):
- The `publish/linux-x64/` folder (the build output)
- The `packaging/linux/` folder (desktop entry, MIME type, build script)

## Step 3: Package as AppImage (on Linux VM)

One-time setup: install FUSE (required to run AppImage tools) and [appimagetool](https://github.com/AppImage/appimagetool):

```bash
sudo apt install libfuse2
wget https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage
chmod +x appimagetool-x86_64.AppImage
sudo mv appimagetool-x86_64.AppImage /usr/local/bin/appimagetool
```

Then `cd` into the folder that contains both `publish/` and `packaging/`, and run the script with the version number from `Directory.Build.props`:

```bash
cd ~/Downloads
chmod +x packaging/linux/build-appimage.sh
sed -i 's/\r$//' packaging/linux/*.sh packaging/linux/*.desktop packaging/linux/*.xml
./packaging/linux/build-appimage.sh 2.0.8
```

This produces `publish/ArgoBooks-2.0.8-linux-x64.AppImage`.
