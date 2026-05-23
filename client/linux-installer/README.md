# Unofficial Linux Install Script for the Net-7 Entertainment Inc. Earth &amp; Beyond Emulator

For updates, issues, and PRs:\
https://github.com/ciphersimian/enb-linux-installer

To install (I strongly encourage you to review the script first, you should never run a script directly from the internet like this without reviewing it first!):\

```
sh <(curl --fail --silent --show-error --location https://raw.githubusercontent.com/ciphersimian/enb-linux-installer/master/install-enb-linux.sh)
```
-or-
```
sh <(wget --no-verbose --output-document=- https://raw.githubusercontent.com/ciphersimian/enb-linux-installer/master/install-enb-linux.sh)
```

This script installs and configures everything necessary to run the Earth &amp; Beyond Emulator on Linux using WINE.

It was based entirely on Nimsy's excellent but difficult to find guide buried on page 7 of a long and winding forum thread:\
https://forum.enb-emulator.com/index.php?/topic/66-linuxmaybe-macwine-install-guide/&do=findComment&comment=91615

I've made some small changes based on my experience (which probably mainly accounts for 7 years of changes between that post and now) but the content of this script is largely a verbatim automation of the steps in Nimsy's guide.

My goal was to reduce user interaction to as close to zero as possible (e.g. using silent install methods for each component, pre-configuring things per Nimsy's guide).  There are only a few prompts near the end, otherwise it's 100% automated and takes ~6-10 minutes (depending on your internet connection).

It will check for and install the following packages if missing:
* `curl`
* `sha256sum`
* `mesa-utils`
* `wine-gecko`
* `wine`

Everything will be installed into a new WINE prefix `~/.wine-enb`; you will be prompted to remove this if it already exists.  If you don't remove it, the script will attempt to modify the existing installation accordingly but this is not recommended.

If you have a freedesktop.org-compliant desktop environment it will create/update the application shortcuts and create links to the wine launcher scripts here:

```
# ${HOME}/.local/bin/enb-launcher   # Start the Net-7 Launcher (to perform updates or launch the game)
# ${HOME}/.local/bin/enb-cfg        # Start Net-7 Config (improved version of Earth & Beyond Config)
# ${HOME}/.local/bin/enb-csc        # Start the Character and Starship Creator
# ${HOME}/.local/bin/enb            # Start the game directly with Net-7 Proxy without the Net-7 Launcher
```

This should put them in the `PATH` so you can start them directly by running the commands `enb`, `enb-launcher`, etc.

The goal was to fundamentally support all popular Linux distros (`ID_LIKE=` `arch`, `debian`, `gentoo`, `rhel`, or `suse`) so the package management is abstracted, though it was only tested (and therefore, only works) on Manjaro (rolling release based on arch) on 2024-12-15 with:

```
$ wine --version
wine-9.20
```

# Limitations &amp; Troubleshooting

Earth &amp; Beyond is a 32-bit game and therefore requires 32-bit support from video and audio drivers.  This is typically not installed with drivers by default and is beyond the scope of this script as the exact packages needed will vary widely depending on the hardware and drivers being used.  Typically it will require installing "lib32" versions of the related drivers and you should be able to find information about this by searching for "lib32" and the related manufacturer or "lib32" and alsa or pipewire, etc. depending on the audio stack you're using.

This script will undoubtedly need tweaks on other distros and probably even on other Manjaro systems (I have multiple and the process for each varied based on what had been installed and how it was configured previously).  I tried to account for those things as much as possible, but you will probably have to tweak things anyway.  It will not update the OS or package managers in any way, so you should do that as appropriate for your distro before installing (as with any other software) to ensure your system is in sync with upstream packages which may be needed to meet dependencies:
* `sudo pacman --sync --refresh --sysupgrade` (Arch/Manjaro, -Syu)
* `sudo apt update && sudo apt upgrade` (Debian/Ubuntu)
* `sudo emaint --auto sync && sudo emerge --ask --verbose --update --deep --newuse @world` (Gentoo)
* `sudo dnf upgrade --refresh` (RHEL/Fedora)
* `sudo zypper update` (openSUSE Leap) or `sudo zypper dist-upgrade` (openSUSE Tumbleweed)

This represents a fundamental change to the state of your system which is why I didn't include it in the script.

By far the most complicated and unpredictable part is installing wine and its dependencies (which is done first) so if you already have that installed and working then it's far more likely the rest of the script will work for you on any distro.  It assumes that you have configured things such that there is a wine package available through whatever package manager your distro uses.

This script strives to be idempotent; running it multiple times will additively bring the system to the desired state and skip all the time-consuming things which already appear to be installed or completed from prior executions (or manual intervention) like downloading and installing various components.  If something didn't work and you have corrected other things since it may be useful to remove/uninstall prior portions so the script will run them again.

MacOS (Darwin) is not supported on account of Apple's ongoing attempts to kill OpenGL in favor of Metal and the difficulty/inability of running a 32-bit WINE prefix.  If you can produce steps for reliably installing and running on recent MacOS versions open an issue and I will consider including them.
