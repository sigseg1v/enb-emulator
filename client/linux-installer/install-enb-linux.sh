#!/bin/sh
#
# Unofficial Linux Install Script for the Net-7 Entertainment Inc. Earth & Beyond Emulator
#
# *** MODIFIED FROM UPSTREAM (GPLv3 §5(a)) ***
# This file is a local fork of the upstream script linked below. The enb-emulator
# project carries local patches on top of upstream; see LOCAL_MODIFICATIONS.md
# next to this file for the diff list, dates, and rationale. Re-syncing from
# upstream requires merging or re-applying those patches.
#
# Upstream source (canonical):
# https://github.com/ciphersimian/enb-linux-installer
#
# For updates, issues, and PRs:
# https://github.com/ciphersimian/enb-linux-installer
#
# To install (I strongly encourage you to review the script first, you should never run a script directly from the
# internet like this without reviewing it first!):
#
# sh <(curl --fail --silent --show-error --location https://raw.githubusercontent.com/ciphersimian/enb-linux-installer/master/install-enb-linux.sh)
# -or-
# sh <(wget --no-verbose --output-document=- https://raw.githubusercontent.com/ciphersimian/enb-linux-installer/master/install-enb-linux.sh)
#
# This script installs and configures everything necessary to run the Earth & Beyond Emulator on Linux using WINE.
#
# It was based entirely on Nimsy's excellent but difficult to find guide buried on page 7 of a long and winding forum
# thread:
# https://forum.enb-emulator.com/index.php?/topic/66-linuxmaybe-macwine-install-guide/&do=findComment&comment=91615
#
# I've made some small changes based on my experience (which probably mainly accounts for 7 years of changes between
# that post and now) but the content of this script is largely a verbatim automation of the steps in Nimsy's guide.
#
# My goal was to reduce user interaction to as close to zero as possible (e.g. using silent install methods for each
# component, pre-configuring things per Nimsy's guide).  There are only a few prompts near the end, otherwise it's 100%
# automated and takes ~6-10 minutes (depending on your internet connection).
#
# It will check for and install the following packages if missing:
# * cabextract
# * curl
# * sha256sum
# * mesa-utils
# * wine-gecko
# * wine
#
# Everything will be installed into a new WINE prefix ~/.wine-enb; you will be prompted to remove this if it already
# exists.  If you don't remove it, the script will attempt to modify the existing installation accordingly but this is
# not recommended.
#
# If you have a freedesktop.org-compliant desktop environment it will create/update the application shortcuts and create
# links to the wine launcher scripts here:
#
# ${HOME}/.local/bin/enb-launcher   # Start the Net-7 Launcher (to perform updates or launch the game)
# ${HOME}/.local/bin/enb-cfg        # Start Net-7 Config (improved version of Earth & Beyond Config)
# ${HOME}/.local/bin/enb-csc        # Start the Character and Starship Creator
# ${HOME}/.local/bin/enb            # Start the game directly with Net-7 Proxy without the Net-7 Launcher
#
# This should put them in the PATH so you can start them directly by running the commands 'enb', enb-launcher', etc.
#
# The goal was to fundamentally support all popular Linux distros (ID_LIKE arch, debian, gentoo, rhel, or suse) so the
# package management is abstracted, though it was only tested (and therefore, only works) on Manjaro (rolling release
# based on arch) on 2025-08-02 with:
#
# $ wine --version
# wine-10.12
#
# Limitations & Troubleshooting
# =============================
# Earth & Beyond is a 32-bit game and therefore requires the [WoW64](https://en.wikipedia.org/wiki/WoW64)
# support from WINE in recent versions. If you are using an older version of WINE see previous versions of
# this repo where WINEARCH=win32 was used.
#
# This script will undoubtedly need tweaks on other distros and probably even on other Manjaro systems (I have multiple
# and the process for each varied based on what had been installed and how it was configured previously).  I tried to
# account for those things as much as possible, but you will probably have to tweak things anyway.  It will not update
# the OS or package managers in any way, so you should do that as appropriate for your distro before installing (as with
# any other software) to ensure your system is in sync with upstream packages which may be needed to meet dependencies:
# * sudo pacman --sync --refresh --sysupgrade (Arch/Manjaro, -Syu)
# * sudo apt update && sudo apt upgrade (Debian/Ubuntu)
# * sudo emaint --auto sync && sudo emerge --ask --verbose --update --deep --newuse @world (Gentoo)
# * sudo dnf upgrade --refresh (RHEL/Fedora)
# * sudo zypper update (openSUSE Leap) or sudo zypper dist-upgrade (openSUSE Tumbleweed)
#
# This represents a fundamental change to the state of your system which is why I didn't include it in the script.
#
# By far the most complicated and unpredictable part is installing wine and its dependencies (which is done first) so if
# you already have that installed and working then it's far more likely the rest of the script will work for you on any
# distro.  It assumes that you have configured things such that there is a wine package available through
# whatever package manager your distro uses.
#
# This script strives to be idempotent; running it multiple times will additively bring the system to the desired state
# and skip all the time-consuming things which already appear to be installed or completed from prior executions (or
# manual intervention) like downloading and installing various components.  If something didn't work and you have
# corrected other things since it may be useful to remove/uninstall prior portions so the script will run them again.
#
# MacOS (Darwin) is not supported on account of Apple's ongoing attempts to kill OpenGL in favor of Metal and
# the difficulty/inability of running 32-bit applications there.  If you can produce steps for reliably
# installing and running on recent MacOS versions open an issue and I will consider including them.

set -o errexit  # -e exit on command errors (so you MUST handle exit codes properly!)
set -o nounset  # -u treat unset variables as an error and exit immediately

# ${ID} and ${ID_LIKE}
case "$(uname -s)" in
    Linux)
        # shellcheck disable=SC1091
        . /etc/os-release ;;
    *)
        error_exit "Unsupported kernel name!: '$(uname -s)'" ;;
esac

# Defaults and command line options
: "${VERBOSE:=}"
: "${DEBUG:=}"

# Get command info
CMD="${0}"
CMD_BASE="$(basename "${CMD}")"
CMD_HOSTNAME="$(hostname -s)"

# Basic helpers
out() { printf "%s %s: %s\n" "$(date '+%F %T')" "${CMD_HOSTNAME}" "${*}" ; }
err() { out ">> ERROR: ${*}" 1>&2 ; }
vrb() { [ -z "${VERBOSE}" ] || out "VERBOSE: ${*}" 1>&2 ; }
dbg() { [ -z "${DEBUG}" ] || out "DEBUG: ${*}" 1>&2 ; }
banner()
{
    printf '%0.s#' $(seq 1 80)
    printf '\n# %s\n' "${*}"
    printf '%0.s#' $(seq 1 80)
    printf '\n\n'
}

pf()
{
    if ! "${@}" ; then
        pipefail_error=${?}
        err "pipefail ${*}"
        exit "${pipefail_error}"
    fi
}

# Exit handler
add_exit_cmd()
{
    new_cmd="${*}"
    vrb "add_exit_cmd: new_cmd: '${new_cmd}'"
    # Some shells, e.g. dash, do not respect trap behavior when run in a subshell with no params and clear the traps
    # anyway; in order to get around that use a temporary file instead.
    #
    # Oh the irony, since the whole reason this function exists is to ensure that we clean up temp files like this no
    # matter what, but we won't be able to do that here... so hopefully the script never dies in this section!
    ADD_EXIT_CMD_TEMP="$(mktemp /tmp/add_exit_cmd_XXXXXXXXXX)"
    trap > "${ADD_EXIT_CMD_TEMP}"
    eval "set -- $(pf cat "${ADD_EXIT_CMD_TEMP}" | pf grep -E 'EXIT$' | sed 's/ EXIT$//')"
    rm -f "${ADD_EXIT_CMD_TEMP}"
    shift # trap
    shift # --
    vrb "add_exit_cmd: \${*}: '${*}'"
    trap -- "${new_cmd} ${new_cmd:+;} ${*}" EXIT
}

on_exit()
{
    EXIT_STATUS="${EXIT_STATUS:-$?}"
    if [ "${EXIT_STATUS}" -ne 0 ]; then
        err "EXIT_STATUS: ${EXIT_STATUS}"
    fi
    trap '' EXIT ABRT HUP INT QUIT TERM
    exit "${EXIT_STATUS}"
}
trap on_exit EXIT
sig_cleanup() { EXIT_STATUS="${EXIT_STATUS:-${?}}" ; trap '' EXIT ABRT HUP INT QUIT TERM ; on_exit ; }
trap sig_cleanup ABRT HUP INT QUIT TERM

error_exit()
{
    err "${@}"
    exit 1
}

wait_for_response()
{
    echo "${*}, press any key to continue"

    if [ -t 0 ] ; then
        saveterm="$(stty -g)"
        stty raw
        stty -echo -icanon min 1 time 0
        dd ibs=1 count=1 >/dev/null 2>/dev/null
        stty -icanon min 0 time 0
        while read -r choice ; do
            true
        done
        stty "${saveterm}"
    fi
}

prompt_for_yes()
{
    printf '%s [y/n]? ' "${*}"
    read -r choice

    if [ "${choice}" != "${choice#[Yy]}" ] ; then
        true
    else
        false
    fi
}

download()
{
    location="${1}"
    output="${2}"

    if command -v "curl" >/dev/null 2>&1 ; then
        curl --fail --location "${location}" --output "${output}"
    elif command -v "wget" >/dev/null 2>&1 ; then
        wget --no-verbose --output-document "${output}" "${location}"
    else
        install_pkg curl
        download "${@}"
    fi
}

checksum()
{
    file="${1}"
    expected="${2:-}"

    if command -v sha256sum >/dev/null 2>&1 ; then
        checksum_command='sha256sum'
    elif command -v sha256 >/dev/null 2>&1 ; then
        checksum_command='sha256'
    elif command -v shasum >/dev/null 2>&1 ; then
        checksum_command='shasum -a 256'
    else
        install_pkg sha256sum
        checksum "${@}"
    fi

    actual="$(pf "${checksum_command}" < "${file}" | cut -d' ' -f1)"
    echo "${actual}"
    if [ -n "${expected}" ] && [ "${actual}"x != "${expected}"x ] ; then
        return 1
    else
        return 0
    fi
}

# Show help function to be used below
show_help()
{
    awk 'NR>1{print} /^(###|$)/{exit}' "${CMD}"
    echo "USAGE: ${CMD_BASE} [arguments]"
    echo "ARGS:"
    MSG="$(pf awk '/^while/,/^esac ; done/' "${CMD}" | pf sed -e 's/^[[:space:]]*/  /' -e 's/|/, /' -e 's/)//' | pf grep '^  -' | grep -v '^  -\*')"
    EMSG="$(eval "echo \"$MSG\"")"
    echo "$EMSG"
}

WINEPREFIX="${WINEPREFIX:-${HOME}/.wine-enb}"

# Parse command line options (odd formatting to simplify show_help() above)
while [ "${#}" -ne 0 ] ; do
    case "${1}" in
        # SWITCHES
        -h|--help)      # This help message
            show_help ; exit 1 ;;
        -d|--debug)     # Enable debugging messages (implies verbose)
            DEBUG="$(( DEBUG + 1 ))" && VERBOSE="${DEBUG}" && echo "#-INFO: DEBUG=${DEBUG} (implies VERBOSE=${VERBOSE})" && shift ;;
        -v|--verbose)   # Enable verbose messages
            VERBOSE="$(( VERBOSE + 1 ))" && echo "#-INFO: VERBOSE=${VERBOSE}" && shift ;;
        -p|--prefix)    # Set the absolute path to the WINEPREFIX (DEFAULT: WINEPREFIX=${WINEPREFIX})
            WINEPREFIX="${2}" && vrb "#-INFO: WINEPREFIX=${WINEPREFIX}" && shift 2;;
        --)              # end argument parsing
            shift && break ;;
        -*)
            show_help ; die "Error: Unsupported flag/argument ${1}" ;;
        *)
            show_help ; die "Unknown option: ${1}" ;;
    esac
done

[ "${DEBUG}" ] && set -x

if [ "$(id -u)" -eq 0 ] ; then
    error_exit "$(cat <<EOF
This script should not be run as root.
Most of what it does needs to happen as the normal user who will run the game and related tools.
You will only be prompted for sudo access in the event that additional OS packages are required.
EOF
)"
fi

################################################################################
# PACKAGE UPDATE ABSTRACTION
################################################################################

case "${ID_LIKE}" in
    *arch*)
        ID_LIKE_ARCH=1 &&
        PACKAGE_QUERY_COMMAND="pacman --query " &&
        PACKAGE_INSTALL_COMMAND="sudo pacman --sync --noconfirm " ;;
    *debian*)
        PACKAGE_QUERY_COMMAND="apt list --installed " &&
        PACKAGE_INSTALL_COMMAND="sudo DEBIAN_FRONTEND=noninteractive apt --assume-yes install --install-recommends " ;;
    *gentoo*)
        PACKAGE_QUERY_COMMAND="qlist --nocolor --installed " &&
        PACKAGE_INSTALL_COMMAND="sudo emerge --ask n " ;;
    *rhel*)
        PACKAGE_QUERY_COMMAND="dnf list installed " &&
        PACKAGE_INSTALL_COMMAND="sudo yum --assumeyes install " ;;
    *suse*)
        PACKAGE_QUERY_COMMAND="zypper search --installed-only " &&
        PACKAGE_INSTALL_COMMAND="sudo zypper install --no-confirm " ;;
    *)
        error_exit "Unknown OS ID:${ID} ID_LIKE:${ID_LIKE}" ;;
esac

is_pkg_installed()
{
    pkg="${1}"
    installed_pkgs="$(eval "${PACKAGE_QUERY_COMMAND}")"
    pf echo "${installed_pkgs}" | grep "${pkg}" 1>&2
}

install_pkg()
{
    pkg="${1}"
    eval "${PACKAGE_INSTALL_COMMAND} ${pkg}" 1>&2
}

find_wine()
{
    if command -v wine ; then
        true
    else
        false
    fi
}

install_wine()
{
    if ! find_wine ; then
        err "wine is not installed, attempting to install wine..."
        if install_pkg wine ; then
            find_wine
        else
            error_exit "$(cat <<EOF
Unable to install wine, determine how to install it on your distro!
winehq-* packages are not recommended, but may be the best option on your distro.
EOF
            )"
        fi
    fi
}

patch_winetricks()
{
    # patch Winetricks#2121
    # https://github.com/Winetricks/winetricks/issues/2121
    # shellcheck disable=SC2016
    if grep 'if \[ "${W_OPT_NOCLEAN}" = 1 ]; then' "${WINETRICKS_EXEC}" ; then
        EDIT_WITH_SUDO=''
        if [ ! -w "${WINETRICKS_EXEC}" ] ; then
            EDIT_WITH_SUDO='sudo '
        fi
        # shellcheck disable=SC2016
        ${EDIT_WITH_SUDO} sed -i -e 's/if \[ "${W_OPT_NOCLEAN}" = 1 ]; then/if [ -z "${W_OPT_NOCLEAN}" ]; then/' "${WINETRICKS_EXEC}"
    fi
}

# https://forum.enb-emulator.com/index.php?/topic/66-linuxmaybe-macwine-install-guide/&do=findComment&comment=91573
# "If you foolishly installed EnB into program files directory"
#                                      - karu
#
# LOL - I took this advice initially while following Nimsy's excellent instructions and all I have to say is that if you
# change these paths you will suffer FAR more than if you leave them alone and deal with the Windows-centric spaces via
# proper quoting or escaping with backslashes.
#
# ${N7_INSTALL_EXE} is hard-coded to look for this path
# - It won't find your EnB install and won't offer to update it if EnB is not in the default location (though I
#   ultimately opted to use the more reliable and comprehensive ${N7_LAUNCHER_EXE} update process)
# ${N7_LAUNCHER_EXE} is hard-coded to look for the default paths for ClientPath, EnBConfigPath, and CharCreatorPath
# - If you change it you will have to re-browse to the ${ENB_CLIENT_EXE} every time there's an update (several times in
#   a row the first time you install) because it will miraculously forget the location (and all of your other settings)
#   every time there's an update (sigh - I implemented a workaround to restore your previous settings each time the
#   launcher is started)
# - As far as I can tell there is NO WAY to:
#   - pre-create a config file for it
#   - pass configuration options on the command-line
#   - Change ClientPath to a different value even temporarily if the default location exists; ${N7_LAUNCHER_EXE} will
#     just silently replace whatever you set ClientPath to with the default
#   - Change EnBConfigPath or CharCreatorPath; they don't get replaced, but they also don't do anything
#     - If the Character and Starship Creator is not in its default location the Tools menu entry for it will be broken.
#       No amount of changing the user.config can change that.
#       - Even when the Tools menu works for it though, it won't work on Linux because it isn't run with the correct
#         working directory (which is why I was trying to point CharCreatorPath at a script)
#       - Luckily I was able to work around this by replacing the executable itself with a script of the same name (see
#         ${CSC_REDIRECT_EXE})
#     - The workaround I used for the Character and Starship Creator doesn't work for the config executables because
#       ${N7_LAUNCHER_EXE} will clobber them on every update so we're stuck with the default config exe from the Tools
#       menu.
#     - I added an enb-cfg script as a shortcut to start the improved ${CFG_EXE} application.
BIN_DIR="${HOME}/.local/bin"
APP_DIR="${HOME}/.local/share/applications/wine/Programs"
MENU_DIR="${HOME}/.config/menus/applications-merged"

ENB_LINUX_INSTALL_PATH="${WINEPREFIX}/drive_c/Program Files/EA GAMES/Earth & Beyond"
ENB_WINE_INSTALL_PATH='C:\\Program Files\\EA GAMES\\Earth & Beyond'
ENB_LINUX_INSTALL_SOURCE="${ENB_LINUX_INSTALL_PATH} Install"
ENB_WINE_INSTALL_SOURCE=${ENB_WINE_INSTALL_PATH}' Install'
ENB_LINK="${BIN_DIR}/enb"
ENB_CLIENT_EXE='client.exe'
# shellcheck disable=SC1003
ENB_WINE_CLIENT_PATH_EXE=${ENB_WINE_INSTALL_PATH}'\\release\\'${ENB_CLIENT_EXE}
ENB_CLIENT_INSTALL_EXE='eandb_demo.exe'
ENB_CLIENT_DL="${ENB_LINUX_INSTALL_SOURCE}/${ENB_CLIENT_INSTALL_EXE}"
ENB_APP_DIR="${APP_DIR}/EA GAMES/Earth & Beyond"

DEMO_LINUX_INSTALL_SOURCE="${ENB_LINUX_INSTALL_SOURCE}/demo"
DEMO_WINE_INSTALL_SOURCE=${ENB_WINE_INSTALL_SOURCE}'\\demo'

N7_LINUX_INSTALL_PATH="${WINEPREFIX}/drive_c/Program Files (x86)/Net-7"
N7_WINE_INSTALL_PATH='C:\\Program Files (x86)\\Net-7'
N7_LINUX_CONFIG_PATH="${WINEPREFIX}/drive_c/users/${USER}/AppData/Local/LaunchNet7"
N7_LAUNCHER_EXE='LaunchNet7.exe'
N7_LAUNCHER_SCRIPT="${N7_LINUX_INSTALL_PATH}/bin/${N7_LAUNCHER_EXE}_wine_launcher.sh"
N7_PROXY_EXE='net7proxy.exe'
N7_PROXY_SCRIPT="${N7_LINUX_INSTALL_PATH}/bin/${N7_PROXY_EXE}_wine_launcher.sh"
N7_LAUNCHER_LINK="${BIN_DIR}/enb-launcher"
N7_INSTALL_EXE='Net-7_Install.exe'
N7_DL="${ENB_LINUX_INSTALL_SOURCE}/${N7_INSTALL_EXE}"
N7_SERVER_HOSTNAME="sunrise.net-7.org"
N7_APP_DIR="${APP_DIR}/Net-7 Entertainment/EnB Emulator"

CFG_LINUX_INSTALL_PATH="${ENB_LINUX_INSTALL_PATH}/EBCONFIG"
CFG_WINE_INSTALL_PATH=${ENB_WINE_INSTALL_PATH}'\\EBCONFIG'
CFG_LINK="${BIN_DIR}/enb-cfg"
CFG_EXE='net7config.exe'
CFG_SCRIPT="${CFG_LINUX_INSTALL_PATH}/${CFG_EXE}_wine_launcher.sh"
CFG_LINUX_EXECUTABLE="${CFG_LINUX_INSTALL_PATH}/E&BConfig.exe"

CSC_LINUX_INSTALL_SOURCE="${ENB_LINUX_INSTALL_SOURCE}/csc"
CSC_WINE_INSTALL_SOURCE=${ENB_WINE_INSTALL_SOURCE}'\\csc'
CSC_LINUX_INSTALL_PATH="${ENB_LINUX_INSTALL_PATH}/Character and Starship Creator"
CSC_LINUX_AVATAR_PATH="${WINEPREFIX}/drive_c/ProgramData/Westwood Studios/Earth and Beyond/Character and Starship Creator"
CSC_WINE_INSTALL_PATH=${ENB_WINE_INSTALL_PATH}'\\Character and Starship Creator'
CSC_LINK="${BIN_DIR}/enb-csc"
CSC_LINUX_EXE='Character and Starship Creator.exe'
CSC_LINUX_PATH_EXE="${CSC_LINUX_INSTALL_PATH}/${CSC_LINUX_EXE}"
CSC_REDIRECT_EXE='CnSC.exe'
CSC_SCRIPT="${ENB_LINUX_INSTALL_PATH}/${CSC_REDIRECT_EXE}_wine_launcher.sh"
CSC_INSTALL_EXE='CharacterStarshipCreator.exe'
CSC_DL="${CSC_LINUX_INSTALL_SOURCE}/${CSC_INSTALL_EXE}"

banner 'UNOFFICIAL LINUX INSTALL SCRIPT FOR THE NET-7 EARTH & BEYOND EMULATOR'

banner 'WINE AND DEPENDENCIES INSTALL'

out "Check for multilib"
if [ -n "${ID_LIKE_ARCH:-}" ]; then
    if ! grep '^\[multilib\]' /etc/pacman.conf ; then
        error_exit "$(cat << EOF
On Arch and derivatives the multilib repo must be enabled to install wine!

This is a significant change to the OS and therefore not automatic; enable
multilib as documented below and then re-run this script:

https://wiki.archlinux.org/title/Official_repositories#Enabling_multilib
EOF
)"
    fi
fi
echo

out "Check for direct rendering"
if ! pf glxinfo | grep 'direct rendering: Yes' ; then
    out "Direct rendering is not available, attempting to install mesa-utils..."
    install_pkg mesa-utils

    if ! pf glxinfo | grep 'direct rendering: Yes' ; then
        error_exit "$(cat <<EOF
Direct rendering is still not available after attempting to install mesa-utils!
Determine how to enable direct rendering on your distro and retry.
EOF
)"
    fi
fi
echo

out "Check for wine"
WINE_EXEC=$(install_wine)
echo

out "Check for wine-gecko (needed by ${N7_LAUNCHER_EXE})"
if ! is_pkg_installed wine-gecko ; then
    out "wine-gecko is not installed, attempting to install wine-gecko..."
    install_pkg wine-gecko || true

    if ! is_pkg_installed wine-gecko ; then
        # failing to find a wine-gecko package, preload the msi installer in the wine cache
        GECKO_VERSION="$(curl --fail --silent --show-error --location \
            'https://source.winehq.org/git/wine.git/blob_plain/HEAD:/dlls/appwiz.cpl/addons.c' \
            | grep '#define GECKO_VERSION' | sed -E 's/.*"(.*)".*/\1/')"
        WINE_CACHE="${HOME}/.cache/wine"
        if [ ! -w "${WINE_CACHE}" ] ; then
            if ! mkdir -p "${WINE_CACHE}" ; then
                error_exit "Could not install wine-gecko nor a usable wine cache (${WINE_CACHE})!"
            fi
        fi
        GECKO_DL="${WINE_CACHE}/wine-gecko-${GECKO_VERSION}-x86.msi"
        if [ ! -e "${GECKO_DL}" ] ; then
            download "https://dl.winehq.org/wine/wine-gecko/${GECKO_VERSION}/$(basename "${GECKO_DL}")" "${GECKO_DL}"
            err "$(cat <<EOF
Unable to install wine-gecko, we did the best we could by loading the gecko msi installer into wine's cache so it can
install it automatically, otherwise determine how to install wine-gecko on your distro!
https://wiki.winehq.org/Gecko
EOF
)"
        fi
    fi
fi
echo

out "Check wine version"
"${WINE_EXEC}" --version
echo

banner 'WINEPREFIX CREATION AND CONFIGURATION'

out "Check for WINEPREFIX"
if [ -e "${WINEPREFIX}" ] ; then
    out "WINEPREFIX '${WINEPREFIX}' already exists!"
    if prompt_for_yes "Permanently remove '${WINEPREFIX}' so this script can recreate it from scratch" ; then
        if ! rm --interactive=never --recursive "${WINEPREFIX}" ; then
            error_exit "There was an error while trying to remove WINEPREFIX '${WINEPREFIX}'"
        fi
    fi
fi
echo

out "Create WINEPREFIX"
# WINEDLLOVERRIDES=mscoree=d prevents mono install during prefix creation
output=$(WINEPREFIX="${WINEPREFIX}" WINEDLLOVERRIDES=mscoree=d "$(dirname "${WINE_EXEC}")/wineboot" 2>&1) || {
    rc="${?}"; err "rc: ${rc}, output: ${output}"; exit "${rc}"
}
echo

# we're not exhaustively checking all the winetricks dependencies here since we use a very limited amount of winetricks
# functionality; we'll just add them opportunistically as people run into issues
out "Check for winetricks dependencies"
if ! command -v cabextract >/dev/null 2>&1 ; then
    install_pkg cabextract

    if ! command -v cabextract >/dev/null 2>&1 ; then
        err "Unable to install cabextract, determine how to install it on your distro!"
    fi
fi
echo

out "Check for winetricks"
# we just download winetricks from github and put it in the prefix to avoid needing sudo and stupid dependency issues
# with Ubuntu
WINETRICKS_EXEC="${WINEPREFIX}/winetricks"
if [ ! -e "${WINETRICKS_EXEC}" ] ; then
    out "winetricks is not present, downloading winetricks..."
    WINETRICKS_URL='https://raw.githubusercontent.com/Winetricks/winetricks/master/src/winetricks'
    download "${WINETRICKS_URL}" "${WINETRICKS_EXEC}"
    patch_winetricks
    if [ ! -e "${WINETRICKS_EXEC}" ] ; then
        error_exit "$(cat <<EOF
Unable to download winetricks from '${WINETRICKS_URL}' to '${WINETRICKS_EXEC}'!
EOF
)"
    fi
fi

# make sure winetricks is executable
if [ ! -x "${WINETRICKS_EXEC}" ] ; then
    chmod a+x "${WINETRICKS_EXEC}"
fi

# make sure winetricks has recently been updated
if [ "$(stat -c %Y -- "${WINETRICKS_EXEC}")" -lt "$(( $(date '+%s') - 172800 ))" ] ; then
    WINE="${WINE_EXEC}" "${WINETRICKS_EXEC}" --self-update
    patch_winetricks
fi

WINE="${WINE_EXEC}" "${WINETRICKS_EXEC}" --version

echo

out "Install dependencies in WINEPREFIX (this will take several minutes)"
# https://forum.enb-emulator.com/index.php?/topic/66-linuxmaybe-macwine-install-guide/&do=findComment&comment=91615
# Nimsy reported:
#
# "Run winecfg (under Graphics, you may want to uncheck all of the Window settings, as I have encountered errors with
# them enabled)"
#
# which means problems with windowmanagerdecorated=y windowmanagermanaged=y (defaults) but I had the exact opposite
# experience ; with windowmanagerdecorated=n windowmanagermanaged=n I was frequently running into bugs with applications
# where a modal dialog would appear, could not be interacted with, then would end up behind the application it was modal
# with (e.g. clicking or alt-tabbing) and become completely impossible to interact with, forcing me to kill the
# application.  I think the default settings are preferable here, but if you have problems you might try what worked for
# Nimsy.
#
# vcrun2008 required by ${N7_LAUNCHER_EXE}
if [ ! -e "${WINEPREFIX}/winetricks.log" ] || ! grep vcrun2008 "${WINEPREFIX}/winetricks.log" ; then
    output=$(WINEPREFIX="${WINEPREFIX}" WINE="${WINE_EXEC}" ${WINETRICKS_EXEC} -q winxp windowmanagerdecorated=y windowmanagermanaged=y \
        dotnet20 corefonts vcrun2008 2>&1) || {
        rc="${?}"; err "rc: ${rc}, output: ${output}"; exit "${rc}"
    }
fi
echo

banner 'GAME AND DEPENDENCIES INSTALLATION'

banner 'Earth & Beyond Client Install'

out "Earth & Beyond Client will be installed into '${ENB_LINUX_INSTALL_PATH}' ('${ENB_WINE_INSTALL_PATH}')"
mkdir -p "${DEMO_LINUX_INSTALL_SOURCE}"
mkdir -p "${CSC_LINUX_INSTALL_SOURCE}"
echo

out "Check for Earth & Beyond Client download"
if [ ! -e "${ENB_CLIENT_DL}" ] ; then
    out "Downloading Earth & Beyond Client"
    download "https://downloads.net-7.org/${ENB_CLIENT_INSTALL_EXE}" "${ENB_CLIENT_DL}"
fi
echo

out "Verify checksum of Earth & Beyond Client (could take a moment)"
if ! checksum "${ENB_CLIENT_DL}" 'dbb729c252ab21cbf85045bdcb8c0ef05611edcedc28029118fa877ad094a3c8' ; then
    error_exit "$(cat <<EOF
The sha265sum of '${ENB_CLIENT_DL}' is invalid!
This could be dangerous; it could be an incomplete/corrupted download or tampering, check the file.
EOF
)"
fi
chmod a+x "${ENB_CLIENT_DL}"
echo

out "Installing Earth & Beyond Client"
echo

out "Extract Earth & Beyond Client from Wise Installer"
if [ ! -e "${CFG_LINUX_EXECUTABLE}" ] ; then
    # double quoting this breaks it; somehow it's being handled
    # shellcheck disable=SC2086
    output=$(WINEPREFIX="${WINEPREFIX}" ${WINE_EXEC} start /wait \
        "${ENB_WINE_INSTALL_SOURCE}\\${ENB_CLIENT_INSTALL_EXE}" /S /X ${DEMO_WINE_INSTALL_SOURCE} 2>&1) || {
        rc="${?}"; err "rc: ${rc}, output: ${output}"; exit "${rc}"
    }
fi
echo

out "Create Earth & Beyond Client Unattended InstallShield Script"
cat <<EOF > "${DEMO_LINUX_INSTALL_SOURCE}/setup.iss"
[{F788D81C-F5EC-4CBE-B1D6-C98E2B8EC7E9}-DlgOrder]
Dlg0={F788D81C-F5EC-4CBE-B1D6-C98E2B8EC7E9}-SdWelcome-0
Count=5
Dlg1={F788D81C-F5EC-4CBE-B1D6-C98E2B8EC7E9}-SdAskDestPath-0
Dlg2={F788D81C-F5EC-4CBE-B1D6-C98E2B8EC7E9}-SdSelectFolder-0
Dlg3={F788D81C-F5EC-4CBE-B1D6-C98E2B8EC7E9}-SdStartCopy-0
Dlg4={F788D81C-F5EC-4CBE-B1D6-C98E2B8EC7E9}-SdFinish-0
[{F788D81C-F5EC-4CBE-B1D6-C98E2B8EC7E9}-SdWelcome-0]
Result=1
[{F788D81C-F5EC-4CBE-B1D6-C98E2B8EC7E9}-SdAskDestPath-0]
szDir=${ENB_WINE_INSTALL_PATH}\\
Result=1
[{F788D81C-F5EC-4CBE-B1D6-C98E2B8EC7E9}-SdSelectFolder-0]
szFolder=EA GAMES\\Earth & Beyond
Result=1
[{F788D81C-F5EC-4CBE-B1D6-C98E2B8EC7E9}-SdStartCopy-0]
Result=1
[{F788D81C-F5EC-4CBE-B1D6-C98E2B8EC7E9}-SdFinish-0]
Result=1
bOpt1=0
bOpt2=1
EOF
echo

if [ ! -e "${CFG_LINUX_EXECUTABLE}" ] ; then
    out "Run Earth & Beyond Client InstallShield (this will take a few minutes)"
    echo "Don't be alarmed when Megan starts talking!:"
    echo "Incoming Transmission"
    echo "Confirmed"
    echo "Installing"
    echo "Install Complete"
    echo "Mission Accomplished"
    output=$(WINEPREFIX="${WINEPREFIX}" ${WINE_EXEC} start /wait \
        "${DEMO_WINE_INSTALL_SOURCE}\\e&bsetup.exe" /s /sms 2>&1) || {
        rc="${?}"; err "rc: ${rc}, output: ${output}"; exit "${rc}"
    }
    echo
fi

banner 'Net-7 Entertainment Install'

out "Check for Net-7 Unified Installer"
if [ ! -e "${N7_DL}" ] ; then
    out "Downloading Net-7 Unified Installer"
    download "https://downloads.net-7.org/${N7_INSTALL_EXE}" "${N7_DL}"
fi
echo

out "Verify download of Net-7 Unified Installer (could take a moment)"
if ! checksum "${N7_DL}" '60fd2d09a7cf00138b2d122490d16d3802031c056277702cf5b160636fa544eb' ; then
    error_exit "$(cat <<EOF
The sha265sum of '${N7_DL}' is invalid!
This could be dangerous; it could be an incomplete/corrupted download or tampering, check the file. It could also just mean the installer was updated and this script needs to be updated as well to be compatible.
EOF
)"
fi
chmod a+x "${N7_DL}"
echo

out "Checking for Net-7 Launcher"
if [ ! -e "${N7_LINUX_INSTALL_PATH}/bin/${N7_LAUNCHER_EXE}" ] ; then
    out "Installing Net-7 Unified Installer"
    N7_WINETRICKS_VERB="$(mktemp /tmp/n7install_XXXXXXXXXX.verb)"
    add_exit_cmd "rm -f ${N7_WINETRICKS_VERB}"
    N7_WINETRICKS_VERB_NAME=$(pf basename "${N7_WINETRICKS_VERB}" | sed 's/.verb//')
    cat <<EOF > "${N7_WINETRICKS_VERB}"
w_metadata ${N7_WINETRICKS_VERB_NAME} apps title="Net-7 Unified Installer"

load_${N7_WINETRICKS_VERB_NAME}()
{
    w_ahk_do "
        WinWaitActivate(WaitTitle, WaitText:=\"\", WaitTimeout:=2)
        {
            Loop
            {
                WinWaitActive, %WaitTitle%, %WaitText%, %WaitTimeout%
                If ErrorLevel
                {
                    WinActivate, %WaitTitle%, %WaitText%
                }
                Else
                {
                    Break
                }
            }
        }

        SetWinDelay 1000        ; wait extra second after each winwait
        SetTitleMatchMode, RegEx
        Run \"${ENB_WINE_INSTALL_SOURCE}/${N7_INSTALL_EXE}\" /S /D=\"${N7_WINE_INSTALL_PATH}\"
        WinWaitActivate(\"Net-7 - Emulator Setup ahk_exe ${N7_INSTALL_EXE}\", \"\.NET 2\.0 or better already installed!\")
        ControlClick, OK        ; Button1
        WinWaitActivate(\"Net-7 - Emulator Setup ahk_exe ${N7_INSTALL_EXE}\", \"Your version of Windows is Windows .*, your installation will continue now\. we just needed to determine the version to determine if you need special privileges because you're on a system that has User Account Control\. \(UAC\)\")
        ControlClick, OK        ; Button1
        WinWaitActivate(\"Net-7 - Emulator Setup ahk_exe ${N7_INSTALL_EXE}\", \"User "".*"" is in the Administrators group.*Original non-restricted account type: Admin\")
        ControlClick, OK        ; Button1
        WinWaitActivate(\"Net-7 - Emulator Setup ahk_exe ${N7_INSTALL_EXE}\", \"We will now test to see if this is a 32 bit or 64 bit system, 64 bit systems require that the registry keys installed by the client are copied to a second location\.\")
        ControlClick, OK        ; Button1
        WinWaitActivate(\"Net-7 - Emulator Setup ahk_exe ${N7_INSTALL_EXE}\", \"Your system is 64-bit, some additional registry keys will be required\.\")
        ControlClick, OK        ; Button1
        WinWaitActivate(\"Net-7 - Emulator Setup ahk_exe ${N7_INSTALL_EXE}\", \"Last but not least, you must register your game accounts\. Hopefully you've done this already, but if not we'll go ahead and open the websites for you to do so\.\")
        ControlClick, OK        ; Button1
        WinWaitActivate(\"Net-7 - Emulator Setup ahk_exe ${N7_INSTALL_EXE}\", \"Did you already register?\")
        ControlClick, Yes       ; Button1
    "
}
EOF
    output="$(WINEPREFIX=${WINEPREFIX} WINE="${WINE_EXEC}" ${WINETRICKS_EXEC} "${N7_WINETRICKS_VERB}" 2>&1)" || {
        rc="${?}"; err "rc: ${rc}, output: ${output}"; exit "${rc}"
    }

    # no longer needed as I decided to just skip enb_up.exe altogether due to it being buggy under wine (in general?)
    # remove blasti99 login default
    #rm --force "${ENB_LINUX_INSTALL_PATH}/Data/client/output/login.ini"
fi
echo

# *** LOCAL MOD (enb-emulator fork, 2026-05-26) ***
# Net-7 Unified Installer drops LaunchNet7.cfg with `http://patch.net-7.org`
# URLs. patch.net-7.org now 301-redirects HTTP -> HTTPS and only accepts
# TLS 1.2+. LaunchNet7.exe targets .NET 2.0/3.5, whose ServicePointManager
# defaults to TLS 1.0 — so the HTTPS handshake fails and the launcher shows
# "Error: 2 / Couldn't connect to the update server. Try again later."
#
# Fix is two parts:
#   1. Rewrite the cfg to point directly at https:// (this block).
#   2. Set the .NET SchUseStrongCrypto + SystemDefaultTlsVersions reg keys
#      so .NET 2.0/4.0 actually negotiate TLS 1.2 (further down in the
#      REGISTRY_CONFIG block in the next section).
# Also bump WINEPREFIX windows-version to win7 here — the script's earlier
# `winetricks winxp` step is required by vcrun2008 and stays as-is, but
# LaunchNet7 (and arguably the patcher GUI) behave more reliably on win7+,
# and the upstream forum recommendation for 2025+ wine is to run as win7.
# See LOCAL_MODIFICATIONS.md and plans/21-phase-u-linux-installer-fixes.md.
N7_LAUNCH_CFG="${N7_LINUX_INSTALL_PATH}/bin/LaunchNet7.cfg"
if [ -e "${N7_LAUNCH_CFG}" ] ; then
    out "[local mod] Patching LaunchNet7.cfg HTTP URLs to HTTPS"
    if grep -q 'http://patch\.net-7\.org' "${N7_LAUNCH_CFG}" ; then
        sed -i.preupgrade-bak 's|http://patch\.net-7\.org|https://patch.net-7.org|g' \
            "${N7_LAUNCH_CFG}"
    else
        echo "  (already patched — skipping)"
    fi
    echo
fi

out "[local mod] Setting WINEPREFIX windows-version to win7 (LaunchNet7 compat)"
output=$(WINEPREFIX="${WINEPREFIX}" ${WINE_EXEC} winecfg -v win7 2>&1) || {
    rc="${?}"; err "rc: ${rc}, output: ${output}"; exit "${rc}"
}
echo

banner 'Character and Starship Creator Install'

out "Check for Character and Starship Creator"
if [ ! -e "${CSC_DL}" ] ; then
    out "downloading Character and Starship Creator"
    download "https://downloads.net-7.org/${CSC_INSTALL_EXE}" "${CSC_DL}"
fi
echo

out "Verify checksum of Character and Starship Creator (could take a moment)"
if ! checksum "${CSC_DL}" '4a9fbb066b8061cff8d2fedc9297c97938e251f878c150fcdf0012166797d142' ; then
    error_exit "$(cat <<EOF
The sha265sum of '${CSC_DL}' is invalid!
This could be dangerous; it could be an incomplete/corrupted download or tampering, check the file.
EOF
)"
fi
chmod a+x "${CSC_DL}"
echo

out "Installing Character and Starship Creator"
echo

out "Create Character and Starship Creator Unattended InstallShield Script"
cat <<EOF > "${CSC_LINUX_INSTALL_SOURCE}/setup.iss"
[{17FF7B21-A872-429C-9331-5883ACD12EE8}-DlgOrder]
Dlg0={17FF7B21-A872-429C-9331-5883ACD12EE8}-SdWelcome-0
Count=5
Dlg1={17FF7B21-A872-429C-9331-5883ACD12EE8}-SdLicense-0
Dlg2={17FF7B21-A872-429C-9331-5883ACD12EE8}-SdAskDestPath-0
Dlg3={17FF7B21-A872-429C-9331-5883ACD12EE8}-SdSelectFolder-0
Dlg4={17FF7B21-A872-429C-9331-5883ACD12EE8}-SdFinish-0
[{17FF7B21-A872-429C-9331-5883ACD12EE8}-SdWelcome-0]
Result=1
[{17FF7B21-A872-429C-9331-5883ACD12EE8}-SdLicense-0]
Result=1
[{17FF7B21-A872-429C-9331-5883ACD12EE8}-SdAskDestPath-0]
szDir=${CSC_WINE_INSTALL_PATH}\\
Result=1
[{17FF7B21-A872-429C-9331-5883ACD12EE8}-SdSelectFolder-0]
szFolder=EA GAMES\\Earth & Beyond
Result=1
[{17FF7B21-A872-429C-9331-5883ACD12EE8}-SdFinish-0]
Result=1
bOpt1=0
bOpt2=0
EOF
echo

if [ ! -e "${CSC_LINUX_PATH_EXE}" ] ; then
    out "Run Character and Starship Creator InstallShield (this will take a few minutes)"
    output=$(WINEPREFIX="${WINEPREFIX}" ${WINE_EXEC} start /wait \
        "${CSC_WINE_INSTALL_SOURCE}\\${CSC_INSTALL_EXE}" /s /sms 2>&1) || {
        rc="${?}"; err "rc: ${rc}, output: ${output}"; exit "${rc}"
    }
    echo
fi

banner 'REGISTRY CONFIGURATION'

REGISTRY_CONFIG="$(mktemp /tmp/XXXXXXXXXX.reg)"
add_exit_cmd "rm -f ${REGISTRY_CONFIG}"

# 1920x1080
# This is the most common resolution in the U.S. but not 4:3 and EnB is a strictly 4:3 aspect ratio game so it will be
# somewhat distorted.
#"RenderDeviceWidth"=dword:00000780
#"RenderDeviceHeight"=dword:00000438
#
# 1280x960 (Default)
# If you have a 1920x1080 monitor playing at this 4:3 resolution windowed is likely going to give you the most accurate
# representation with room for the title bar/taskbar.
#"RenderDeviceWidth"=dword:00000500
#"RenderDeviceHeight"=dword:000003C0
#
# 1400x1050
#"RenderDeviceWidth"=dword:00000578
#"RenderDeviceHeight"=dword:0000041A
#
# 1792x1344
# This works well with a *x1440 monitor.
#"RenderDeviceWidth"=dword:00000700
#"RenderDeviceHeight"=dword:00000540
#
# 1856x1392
#"RenderDeviceWidth"=dword:00000740
#"RenderDeviceHeight"=dword:00000570
cat <<EOF > "${REGISTRY_CONFIG}"
Windows Registry Editor Version 5.00

; 64-bit Prefix
[HKEY_LOCAL_MACHINE\\Software\\Wow6432Node\\Westwood Studios]

[HKEY_LOCAL_MACHINE\\Software\\Wow6432Node\\Westwood Studios\\Earth and Beyond]

[HKEY_LOCAL_MACHINE\\Software\\Wow6432Node\\Westwood Studios\\Earth and Beyond\\Registration]
"Enabled"=dword:00000000
"Registered"=dword:00000001

[HKEY_LOCAL_MACHINE\\Software\\Wow6432Node\\Westwood Studios\\Earth and Beyond\\Render]
"EBConfigVersion"=dword:0000000b
"LandLOD"=dword:00000007
"LandPolyBudget"=dword:00006d60
"N7ConfigAutoDetectPerf"=dword:00000001
"PolyBudget"=dword:00007530
"RenderDeviceDepth"=dword:00000020
"RenderDeviceTextureDepth"=dword:00000020
"RenderDeviceWidth"=dword:00000500
"RenderDeviceHeight"=dword:000003C0
"RenderDeviceWindowed"=dword:00000001
"SettingsTested"=dword:00000001
"StarBudget"=dword:000007d0
"TextureFilter"=dword:00000002
"TextureReduction"=dword:00000000

[HKEY_LOCAL_MACHINE\\Software\\Wow6432Node\\Westwood Studios\\Earth and Beyond\\Sound]
"cinematic enabled"=dword:00000001
"dialog enabled"=dword:00000001
"music enabled"=dword:00000001
"sound enabled"=dword:00000001

; *** LOCAL MOD (enb-emulator fork, 2026-05-26) ***
; Force .NET 2.0/3.5 and .NET 4.x to negotiate modern TLS so LaunchNet7.exe's
; HTTPS request to https://patch.net-7.org/ succeeds. .NET 2.0/3.5 defaults
; to TLS 1.0 only; modern Let's Encrypt-fronted endpoints (including
; patch.net-7.org) reject TLS 1.0/1.1. Without these keys the launcher hangs
; on "Downloading Patch Information…" and then surfaces "Error: 2 / Couldn't
; connect to the update server." Documented Microsoft workaround:
;   SchUseStrongCrypto       — enable strong cipher suites
;   SystemDefaultTlsVersions — defer protocol choice to OS / Wine schannel
; Set under both native and Wow6432Node for 32- and 64-bit .NET runtimes.
[HKEY_LOCAL_MACHINE\\Software\\Microsoft\\.NETFramework\\v2.0.50727]
"SchUseStrongCrypto"=dword:00000001
"SystemDefaultTlsVersions"=dword:00000001

[HKEY_LOCAL_MACHINE\\Software\\Wow6432Node\\Microsoft\\.NETFramework\\v2.0.50727]
"SchUseStrongCrypto"=dword:00000001
"SystemDefaultTlsVersions"=dword:00000001

[HKEY_LOCAL_MACHINE\\Software\\Microsoft\\.NETFramework\\v4.0.30319]
"SchUseStrongCrypto"=dword:00000001
"SystemDefaultTlsVersions"=dword:00000001

[HKEY_LOCAL_MACHINE\\Software\\Wow6432Node\\Microsoft\\.NETFramework\\v4.0.30319]
"SchUseStrongCrypto"=dword:00000001
"SystemDefaultTlsVersions"=dword:00000001
EOF

# add the .reg into the windows registry within the WINEPREFIX
output=$(WINEPREFIX="${WINEPREFIX}" ${WINE_EXEC} start /wait regedit.exe "${REGISTRY_CONFIG}" 2>&1) || {
    rc="${?}"; err "rc: ${rc}, output: ${output}"; exit "${rc}"
}
echo

banner 'CREATE CONVENIENCE SCRIPTS'

sb='<setting name="'
se='</setting>'
sa='serializeAs="String">'
i1='                '
i2='            '
t='</LaunchNet7.Properties.Settings>'
cat <<-EOF > "${N7_LAUNCHER_SCRIPT}"
#!/bin/bash

# Starts the Net-7 Launcher (to perform updates or launch the game)

set -o errtrace # -E pass trap handlers down to subshells
set -o errexit  # -e exit on command errors (so you MUST handle exit codes properly!)
set -o nounset  # -u treat unset variables as an error and exit immediately
set -o pipefail # capture fail exit codes in piped commands

# if the specified file contains the specified setting, update the value, otherwise add it
update_or_add_config_setting()
{
    file="\${1}"
    setting="\$(printf '%q' "\${2}")"
    value="\$(printf '%q' "\${3}")"

    # escape for xml
    value=\${value//\"/\&quot;}
    value=\${value//\'/\&apos;}
    value=\${value//</\&lt;}
    value=\${value//>/\&gt;}
    value=\${value//&/\&amp;}

    if grep "\${setting}" "\${file}" >/dev/null 2>&1 ; then
        # update the existing value
        perl -0777 -p -i -e \\
            's#$sb'"\${setting}"'".+?\R.+?\R.+?$se#$sb'"\${setting}"'" $sa\r\n$i1<value>'"\${value}"'</value>\r\n$i2$se#m' "\${file}"
    else
        # add a new entry
        perl -0777 -p -i -e \\
            's#$t#    $sb'"\${setting}"'" $sa\r\n$i1<value>'"\${value}"'</value>\r\n$i2$se\r\n        $t#m' "\${file}"
    fi
}

# attempts to find the value of the specified setting in the specified file; if the setting is missing/blank/etc:
#
# <value></value>
# <value />
# <value> </value>
#
# then an empty string will be printed to stdout, otherwise the value of the setting will be printed
get_config_value()
{
    file="\${1}"
    setting="\$(printf '%q' "\${2}")"

    # try to locate the setting, save just the value, and throw out the rest of the file
    # if we find what we're looking for we expect only one line
    setting_string="\$(perl -0777 -p -e 's#^.+$sb'"\${setting}"'".+?\R(.+?)\R.+?$se.+\$#\\1#s' "\${file}" | tail -n1)"
    if [ -n "\${setting_string}" ] && [[ "\${setting_string}" = *"value"* ]] && [[ ! "\${setting_string}" = *"/>"* ]] ; then
        re="<value>(.*)</value>"
        if [[ "\${setting_string}" =~ \${re} ]] && [ -n "\${BASH_REMATCH[1]}" ] ; then
            # sed to trim leading and trailing whitespace
            rv="\$(sed -e 's/^[[:space:]]*//;s/[[:space:]]*\$//' <<< "\${BASH_REMATCH[1]}")"

            # expand special xml sequences
            rv=\${rv//&quot;/\"}
            rv=\${rv//&apos;/\'}
            rv=\${rv//&lt;/<}
            rv=\${rv//&gt;/>}
            rv=\${rv//&amp;/\&}

            echo "\${rv}"
        fi
    fi

    echo ""
}

# If the latest config is new (e.g. freshly installed or updated):
#
# Ensure ClientPath, EnBConfigPath, and CharCreatorPath are correct (for all the good it will do)
#
# Default UseExperimentalReorder=True
# This is recommended in many places, example below, should just be the default:
# https://forum.enb-emulator.com/index.php?/topic/13388-installed-but-now-what-mini-guide/
#
# Default DisableMouseLock=False
# On recent versions of wine this works much better, the only reason you would probably want to change this is if you're
# multiboxing.
#
# Migrate all the settings over from the previous config file (so settings are no longer lost on updates)
if [ -d "${N7_LINUX_CONFIG_PATH}" ] ; then
    N7_LATEST_USER_CONFIG=\$(find "${N7_LINUX_CONFIG_PATH}" -type f -name user.config -printf "%T+ %p\\n"| sort | cut -d' ' -f2 | tail -n1)
    N7_LATEST_USER_CONFIG_SEEN="\$(dirname "\${N7_LATEST_USER_CONFIG}")/.\$(basename "\${N7_LATEST_USER_CONFIG}").seen"
    if [ -n "\${N7_LATEST_USER_CONFIG}" ] && [ ! -e "\${N7_LATEST_USER_CONFIG_SEEN}" ] ; then
        echo "Configuring new '\${N7_LATEST_USER_CONFIG}'"

        # we'll look at this later but we want to identify it before we start modifying N7_LATEST_USER_CONFIG
        N7_PREV_USER_CONFIG=\$(find "${N7_LINUX_CONFIG_PATH}" -type f -name user.config -printf "%T+ %p\\n" | sort | cut -d' ' -f2 | tail -n2 | sed '1!d')

        # ClientPath
        update_or_add_config_setting "\${N7_LATEST_USER_CONFIG}" ClientPath "${ENB_WINE_CLIENT_PATH_EXE}"

        # UseExperimentalReorder
        update_or_add_config_setting "\${N7_LATEST_USER_CONFIG}" UseExperimentalReorder True

        # DisableMouseLock
        update_or_add_config_setting "\${N7_LATEST_USER_CONFIG}" DisableMouseLock False

        # EnBConfigPath (doesn't seem to work)
        update_or_add_config_setting "\${N7_LATEST_USER_CONFIG}" EnBConfigPath "${CFG_WINE_INSTALL_PATH}\\${CFG_EXE}"

        # CharCreatorPath (doesn't seem to work)
        update_or_add_config_setting "\${N7_LATEST_USER_CONFIG}" CharCreatorPath "${CSC_WINE_INSTALL_PATH}\\${CSC_LINUX_EXE}"

        if [ -n "\${N7_PREV_USER_CONFIG}" ] && [ "\${N7_PREV_USER_CONFIG}" != "\${N7_LATEST_USER_CONFIG}" ] ; then
            echo "Migrating previous ('\${N7_PREV_USER_CONFIG}') into new ('\${N7_LATEST_USER_CONFIG}')"

            # these are user preferences that should be preserved, ServerList is the only notable exclusion from this
            # list as it is determined upstream, has the potential to change in an update, and the user would want to
            # pick up the new value
            for s in ClientPath UseLocalCert UsePacketOpt UseExperimentalReorder DisableMouseLock \\
                     LastServerName AuthenticationPort UseSecureAuthentication DebugLaunch SelectedIP \\
                     ServerIndex LockPort EnBConfigPath CharCreatorPath DeleteTH6Files FormMainPosition ; do
                # determine if the setting existed in the previous config
                current_value=\$(get_config_value "\${N7_LATEST_USER_CONFIG}" "\${s}")
                prev_value=\$(get_config_value "\${N7_PREV_USER_CONFIG}" "\${s}")
                if [ -n "\${prev_value}" ] && [ "\${current_value}"x != "\${prev_value}"x ] ; then
                    # One downside of this is that settings which were removed will likely break; this is probably not
                    # very common and better to have to fix that manually than fix all your settings after every update.
                    echo "Migrating previous setting \${s}='\${prev_value}', replacing '\${current_value}'"
                    update_or_add_config_setting "\${N7_LATEST_USER_CONFIG}" "\${s}" "\${prev_value}"
                fi
            done
        fi

        touch "\${N7_LATEST_USER_CONFIG_SEEN}"
        echo
    fi
fi

WAIT=\${WAIT:+/wait}

launch()
{
    # shellcheck disable=SC2086
    output=\$(WINEPREFIX="${WINEPREFIX}" ${WINE_EXEC} start /d "${N7_WINE_INSTALL_PATH}\\\\bin" \${WAIT:-} \\
        "${N7_LAUNCHER_EXE}" 2>&1) || {
        rc="\${?}"; echo ">> ERROR: rc: \${rc}, output: \${output}" 1>&2; exit "\${rc}"
    }

    # disable startup movies
    if [ -e "${ENB_LINUX_INSTALL_PATH}/Data/client/mixfiles/EB_Sizzle.bik" ] ; then
        mv --force "${ENB_LINUX_INSTALL_PATH}/Data/client/mixfiles/EB_Sizzle.bik" \\
            "${ENB_LINUX_INSTALL_PATH}/Data/client/mixfiles/EB_Sizzle.bik.bak" 2>/dev/null
        mv --force "${ENB_LINUX_INSTALL_PATH}/Data/client/mixfiles/eb_ws_logo.bik" \\
            "${ENB_LINUX_INSTALL_PATH}/Data/client/mixfiles/eb_ws_logo.bik.bak" 2>/dev/null
    fi
}

if [ -n "\${WAIT:-}" ] ; then
    launch
else
    launch &
fi
EOF

chmod a+x "${N7_LAUNCHER_SCRIPT}"

cat <<-EOF > "${CFG_SCRIPT}"
#!/bin/sh

# Starts Net-7 Config

set -o errexit  # -e exit on command errors (so you MUST handle exit codes properly!)
set -o nounset  # -u treat unset variables as an error and exit immediately

WAIT=\${WAIT:+/wait}

launch()
{
    # shellcheck disable=SC2086
    output=\$(WINEPREFIX="${WINEPREFIX}" ${WINE_EXEC} start /d "${CFG_WINE_INSTALL_PATH}" \${WAIT:-} \\
        "${CFG_EXE}" 2>&1) || {
        rc="\${?}"; echo ">> ERROR: rc: \${rc}, output: \${output}" 1>&2; exit "\${rc}"
    }
}

if [ -n "\${WAIT:-}" ] ; then
    launch
else
    launch &
fi
EOF

chmod a+x "${CFG_SCRIPT}"

if [ ! -e "${CSC_LINUX_INSTALL_PATH}/${CSC_REDIRECT_EXE}" ] ; then
    mv "${CSC_LINUX_PATH_EXE}" "${CSC_LINUX_INSTALL_PATH}/${CSC_REDIRECT_EXE}"
fi

cat <<-EOF > "${CSC_SCRIPT}"
#!/bin/sh

# Starts the Character and Starship Creator

set -o errexit  # -e exit on command errors (so you MUST handle exit codes properly!)
set -o nounset  # -u treat unset variables as an error and exit immediately

WAIT=\${WAIT:+/wait}

launch()
{
    # shellcheck disable=SC2086
    output=\$(WINEPREFIX="${WINEPREFIX}" ${WINE_EXEC} start /d "${CSC_WINE_INSTALL_PATH}" \${WAIT:-} \\
        explorer /desktop=Earth_and_Beyond_Character_and_Starship_Creator,800x600 \\
        "${CSC_REDIRECT_EXE}" -noclassrestrictions 2>&1) || {
        rc="\${?}"; echo ">> ERROR: rc: \${rc}, output: \${output}" 1>&2; exit "\${rc}"
    }
}

if [ -n "\${WAIT:-}" ] ; then
    launch
else
    launch &
fi
EOF

chmod a+x "${CSC_SCRIPT}"
if [ ! -e "${CSC_LINUX_PATH_EXE}" ] ; then
    ln --symbolic "${CSC_SCRIPT}" "${CSC_LINUX_PATH_EXE}"
fi

cat <<-EOF > "${N7_PROXY_SCRIPT}"
#!/bin/bash

# Starts the Net-7 Proxy (starts Earth & Beyond Emulator directly without the launcher)

set -o errtrace # -E pass trap handlers down to subshells
set -o errexit  # -e exit on command errors (so you MUST handle exit codes properly!)
set -o nounset  # -u treat unset variables as an error and exit immediately
set -o pipefail # capture fail exit codes in piped commands

WAIT=\${WAIT:+/wait}

launch()
{
    # disable startup movies
    if [ -e "${ENB_LINUX_INSTALL_PATH}/Data/client/mixfiles/EB_Sizzle.bik" ] ; then
        mv --force "${ENB_LINUX_INSTALL_PATH}/Data/client/mixfiles/EB_Sizzle.bik" \\
            "${ENB_LINUX_INSTALL_PATH}/Data/client/mixfiles/EB_Sizzle.bik.bak" 2>/dev/null
        mv --force "${ENB_LINUX_INSTALL_PATH}/Data/client/mixfiles/eb_ws_logo.bik" \\
            "${ENB_LINUX_INSTALL_PATH}/Data/client/mixfiles/eb_ws_logo.bik.bak" 2>/dev/null
    fi

    # read options from launcher config
    # /DML => Disable Mouse Lock
    # /EXREORDER => Prototype Reorder
    # /POPT => Packet Optimization
    # defaults
    DISABLE_MOUSE_LOCK="" # = False
    USE_EXPERIMENTAL_PROTOTYPE_REORDER="/EXREORDER" # = True
    USE_PACKET_OPTIMIZATION="/POPT" # = True
    if [ -d "${N7_LINUX_CONFIG_PATH}" ] ; then
        N7_LATEST_USER_CONFIG=\$(find "${N7_LINUX_CONFIG_PATH}" -type f -name user.config -printf "%T+ %p\\n"| sort | cut -d' ' -f2 | tail -n1)
        if [ -f "\${N7_LATEST_USER_CONFIG}" ] ; then
            DISABLE_MOUSE_LOCK_SETTING="\$(grep -A1 DisableMouseLock "\${N7_LATEST_USER_CONFIG}" || true)"
            if [ -n "\${DISABLE_MOUSE_LOCK_SETTING}" ] ; then
                if echo "\${DISABLE_MOUSE_LOCK_SETTING}" | grep True >/dev/null 2>&1 ; then
                    DISABLE_MOUSE_LOCK="/DML"
                else
                    DISABLE_MOUSE_LOCK=""
                fi
            fi

            USE_EXPERIMENTAL_PROTOTYPE_REORDER_SETTING="\$(grep -A1 UseExperimentalReorder "\${N7_LATEST_USER_CONFIG}" || true)"
            if [ -n "\${USE_EXPERIMENTAL_PROTOTYPE_REORDER_SETTING}" ] ; then
                if echo "\${USE_EXPERIMENTAL_PROTOTYPE_REORDER_SETTING}" | grep True >/dev/null 2>&1 ; then
                    USE_EXPERIMENTAL_PROTOTYPE_REORDER="/EXREORDER"
                else
                    USE_EXPERIMENTAL_PROTOTYPE_REORDER=""
                fi
            fi

            USE_PACKET_OPTIMIZATION_SETTING="\$(grep -A1 UsePacketOpt "\${N7_LATEST_USER_CONFIG}" || true)"
            if [ -n "\${USE_PACKET_OPTIMIZATION_SETTING}" ] ; then
                if echo "\${USE_PACKET_OPTIMIZATION_SETTING}" | grep True >/dev/null 2>&1 ; then
                    USE_PACKET_OPTIMIZATION="/POPT"
                else
                    USE_PACKET_OPTIMIZATION=""
                fi
            fi
        fi
    fi

    SERVER_IP_ADDRESS="\$( ( ping -c1 -w1 ${N7_SERVER_HOSTNAME} || true ) | sed '1!d' | sed -E 's/.*\\(([0-9]+\\.[0-9]+\\.[0-9]+\\.[0-9]+)\\).*/\\1/')"
    # shellcheck disable=SC2086
    output=\$(WINEPREFIX="${WINEPREFIX}" ${WINE_EXEC} start /d "${N7_WINE_INSTALL_PATH}\\\\bin" \${WAIT:-} \\
        "${N7_PROXY_EXE}" /LADDRESS:0 /ADDRESS:"\${SERVER_IP_ADDRESS}" /CLIENT:"${ENB_WINE_CLIENT_PATH_EXE}" \\
        \${DISABLE_MOUSE_LOCK} \${USE_EXPERIMENTAL_PROTOTYPE_REORDER} \${USE_PACKET_OPTIMIZATION} 2>&1) || {
        rc="\${?}"; echo ">> ERROR: rc: \${rc}, output: \${output}" 1>&2; exit "\${rc}"
    }
}

if [ -n "\${WAIT:-}" ] ; then
    launch
else
    launch &
fi
EOF

chmod a+x "${N7_PROXY_SCRIPT}"

banner 'CREATE AND UPDATE LINKS AND SHORTCUTS'

if [ -n "${XDG_DATA_DIRS:-}" ] ; then
    mkdir -p "${BIN_DIR}"

    out "Create links"
    echo

    if [ -L "${N7_LAUNCHER_LINK}" ] ; then
        rm --force "${N7_LAUNCHER_LINK}"
    fi
    ln --symbolic "${N7_LAUNCHER_SCRIPT}" "${N7_LAUNCHER_LINK}"

    echo "To start the Net-7 Launcher (to perform updates or launch the game):"
    basename "${N7_LAUNCHER_LINK}"
    echo
    echo "which is a symlink to:"
    echo "\"${N7_LAUNCHER_SCRIPT}\""
    echo

    if [ -L "${CFG_LINK}" ] ; then
        rm --force "${CFG_LINK}"
    fi
    ln --symbolic "${CFG_SCRIPT}" "${CFG_LINK}"

    echo "To start Net-7 Config:"
    basename "${CFG_LINK}"
    echo
    echo "which is a symlink to:"
    echo "\"${CFG_SCRIPT}\""
    echo

    if [ -L "${CSC_LINK}" ] ; then
        rm --force "${CSC_LINK}"
    fi
    ln --symbolic "${CSC_SCRIPT}" "${CSC_LINK}"

    echo "To start the Character and Starship Creator:"
    basename "${CSC_LINK}"
    echo
    echo "which is a symlink to:"
    echo "\"${CSC_SCRIPT}\""
    echo

    if [ -L "${ENB_LINK}" ] ; then
        rm --force "${ENB_LINK}"
    fi
    ln --symbolic "${N7_PROXY_SCRIPT}" "${ENB_LINK}"

    echo "To start the Net-7 Proxy (starts Earth & Beyond Emulator directly without the launcher):"
    basename "${ENB_LINK}"
    echo
    echo "which is a symlink to:"
    echo "\"${N7_PROXY_SCRIPT}\""
    echo

    # /home/${USER}/.local/share/applications/wine/Programs/EA GAMES/Earth & Beyond/Character and Starship Creator.desktop
    # /home/${USER}/.local/share/applications/wine/Programs/EA GAMES/Earth & Beyond/Character and Starship Creator ReadMe.desktop
    # /home/${USER}/.local/share/applications/wine/Programs/EA GAMES/Earth & Beyond/Earth & Beyond Configuration.desktop
    # /home/${USER}/.local/share/applications/wine/Programs/EA GAMES/Earth & Beyond/Earth & Beyond.desktop
    # /home/${USER}/.local/share/applications/wine/Programs/EA GAMES/Earth & Beyond/Earth & Beyond Quick Reference.desktop
    # /home/${USER}/.local/share/applications/wine/Programs/EA GAMES/Earth & Beyond/Earth & Beyond ReadMe.desktop
    # /home/${USER}/.local/share/applications/wine/Programs/EA GAMES/Earth & Beyond/Earth & Beyond Website.desktop
    # /home/${USER}/.local/share/applications/wine/Programs/EA GAMES/Earth & Beyond/System Information Utility.desktop
    # /home/${USER}/.local/share/applications/wine/Programs/EA GAMES/Earth & Beyond/Troubleshooting Information.desktop
    # /home/${USER}/.local/share/applications/wine/Programs/EA GAMES/Earth & Beyond/Uninstall Character and Starship Creator.desktop
    # /home/${USER}/.local/share/applications/wine/Programs/EA GAMES/Earth & Beyond/Uninstall Earth & Beyond.desktop
    # /home/${USER}/.local/share/applications/wine/Programs/Net-7 Entertainment/EnB Emulator/LaunchNet7.desktop
    # /home/${USER}/.local/share/applications/wine/Programs/Net-7 Entertainment/EnB Emulator/Net-7 EnB EMU - Forum.desktop
    # /home/${USER}/.local/share/applications/wine/Programs/Net-7 Entertainment/EnB Emulator/Net-7 Portal.desktop

    # /home/${USER}/.config/menus/applications-merged/wine-Programs-EA GAMES-Earth & Beyond-Character and Starship Creator.menu
    # /home/${USER}/.config/menus/applications-merged/wine-Programs-EA GAMES-Earth & Beyond-Character and Starship Creator ReadMe.menu
    # /home/${USER}/.config/menus/applications-merged/wine-Programs-EA GAMES-Earth & Beyond-Earth & Beyond Configuration.menu
    # /home/${USER}/.config/menus/applications-merged/wine-Programs-EA GAMES-Earth & Beyond-Earth & Beyond.menu
    # /home/${USER}/.config/menus/applications-merged/wine-Programs-EA GAMES-Earth & Beyond-Earth & Beyond Quick Reference.menu
    # /home/${USER}/.config/menus/applications-merged/wine-Programs-EA GAMES-Earth & Beyond-Earth & Beyond ReadMe.menu
    # /home/${USER}/.config/menus/applications-merged/wine-Programs-EA GAMES-Earth & Beyond-Earth & Beyond Website.menu
    # /home/${USER}/.config/menus/applications-merged/wine-Programs-EA GAMES-Earth & Beyond-System Information Utility.menu
    # /home/${USER}/.config/menus/applications-merged/wine-Programs-EA GAMES-Earth & Beyond-Troubleshooting Information.menu
    # /home/${USER}/.config/menus/applications-merged/wine-Programs-EA GAMES-Earth & Beyond-Uninstall Character and Starship Creator.menu
    # /home/${USER}/.config/menus/applications-merged/wine-Programs-EA GAMES-Earth & Beyond-Uninstall Earth & Beyond.menu
    # /home/${USER}/.config/menus/applications-merged/wine-Programs-Net-7 Entertainment-EnB Emulator-LaunchNet7.menu
    # /home/${USER}/.config/menus/applications-merged/wine-Programs-Net-7 Entertainment-EnB Emulator-Net-7 EnB EMU - Forum.menu
    # /home/${USER}/.config/menus/applications-merged/wine-Programs-Net-7 Entertainment-EnB Emulator-Net-7 Portal.menu

    # /home/${USER}/.local/share/desktop-directories/wine-Programs.directory
    # /home/${USER}/.local/share/desktop-directories/wine-Programs-EA GAMES.directory
    # /home/${USER}/.local/share/desktop-directories/wine-Programs-EA GAMES-Earth & Beyond.directory
    # /home/${USER}/.local/share/desktop-directories/wine-Programs-Net-7 Entertainment.directory
    # /home/${USER}/.local/share/desktop-directories/wine-Programs-Net-7 Entertainment-EnB Emulator.directory
    # /home/${USER}/.local/share/desktop-directories/wine-wine.directory

    out "Update application shortcuts"
    N7_LAUNCHER_DESKTOP="${N7_APP_DIR}/LaunchNet7.desktop"
    if [ -e "${N7_LAUNCHER_DESKTOP}" ] ; then
        perl -p -i -e "s/^Name=.*/Name=Net-7 Launcher/" "${N7_LAUNCHER_DESKTOP}"
        perl -p -i -e "s#^Exec=.*#Exec=\"${N7_LAUNCHER_SCRIPT}\"#" "${N7_LAUNCHER_DESKTOP}"
        perl -p -i -e "s/^(Path=.+?)(?:\/bin)*$/\1\/bin/" "${N7_LAUNCHER_DESKTOP}"
    fi

    ENB_CONFIG_DESKTOP="${ENB_APP_DIR}/Earth & Beyond Configuration.desktop"
    if [ -e "${ENB_CONFIG_DESKTOP}" ] ; then
        perl -p -i -e "s#^Exec=.*#Exec=\"${CFG_SCRIPT}\"#" "${ENB_CONFIG_DESKTOP}"
    fi

    CSC_DESKTOP="${ENB_APP_DIR}/Character and Starship Creator.desktop"
    if [ -e "${CSC_DESKTOP}" ] ; then
        perl -p -i -e "s#^Exec=.*#Exec=\"${CSC_SCRIPT}\"#" "${CSC_DESKTOP}"
    fi

    CSC_README_DESKTOP="${ENB_APP_DIR}/Character and Starship Creator ReadMe.desktop"
    if [ -e "${CSC_README_DESKTOP}" ] ; then
        # shellcheck disable=SC2016
        sed -i -e '/^\(Icon=\).*/{s//\1libreoffice-writer/;:a;n;ba;q}' -e '$aIcon=libreoffice-writer' "${CSC_README_DESKTOP}"
    fi

    ENB_DESKTOP="${ENB_APP_DIR}/Earth & Beyond.desktop"
    if [ -e "${ENB_DESKTOP}" ] ; then
        perl -p -i -e "s#^Exec=.*#Exec=\"${N7_PROXY_SCRIPT}\"#" "${ENB_DESKTOP}"
        perl -p -i -e "s#^Path=.*\$#$(grep Path "${N7_LAUNCHER_DESKTOP}")#" "${ENB_DESKTOP}"
    fi

    ENB_README_DESKTOP="${ENB_APP_DIR}/Earth & Beyond ReadMe.desktop"
    if [ -e "${ENB_README_DESKTOP}" ] ; then
        # shellcheck disable=SC2016
        sed -i -e '/^\(Icon=\).*/{s//\1libreoffice-writer/;:a;n;ba;q}' -e '$aIcon=libreoffice-writer' "${ENB_README_DESKTOP}"
    fi

    # remove N/A shortcuts
    rm --force "${ENB_APP_DIR}/Earth & Beyond Website.desktop"
    rm --force "${MENU_DIR}/wine-Programs-EA GAMES-Earth & Beyond-Earth & Beyond Website.menu"
    rm --force "${ENB_APP_DIR}/Uninstall Character and Starship Creator.desktop"
    rm --force "${MENU_DIR}/wine-Programs-EA GAMES-Earth & Beyond-Uninstall Character and Starship Creator.menu"
    rm --force "${ENB_APP_DIR}/Uninstall Earth & Beyond.desktop"
    rm --force "${MENU_DIR}/wine-Programs-EA GAMES-Earth & Beyond-Uninstall Earth & Beyond.menu"

    if ( command -v gsettings && env | grep -i gnome && pgrep -i gnome ) >/dev/null 2>&1 ; then
        GNOME_APP_FOLDER=enb
        dot_ogdaf='org.gnome.desktop.app-folders'
        slash_ogdaf='org/gnome/desktop/app-folders/folders'
        # if this fails despite our best efforts above this is probably not really gnome
        if gsettings get "${dot_ogdaf}" folder-children >/dev/null 2>&1 ; then
            if ! pf gsettings get "${dot_ogdaf}" folder-children | grep "${GNOME_APP_FOLDER}" ; then
                EXISTING_GNOME_APP_FOLDERS="$(pf gsettings get "${dot_ogdaf}" folder-children | pf sed 's/[\[]//g' | sed 's/[]]//g')"
                gsettings set "${dot_ogdaf}" folder-children \
                    "[${EXISTING_GNOME_APP_FOLDERS}, '${GNOME_APP_FOLDER}']"
                gsettings set "${dot_ogdaf}"'.folder:/'"${slash_ogdaf}/${GNOME_APP_FOLDER}"'/' name 'Earth & Beyond'
                gsettings set "${dot_ogdaf}"'.folder:/'"${slash_ogdaf}/${GNOME_APP_FOLDER}"'/' categories "['${GNOME_APP_FOLDER}']"
            fi
            if [ -d "${ENB_APP_DIR}" ] ; then
                # shellcheck disable=SC2016
                find "${ENB_APP_DIR}" -type f -name '*.desktop' \
                    -exec sed -i -e '/^\(Categories=\).*/{s//\1'"${GNOME_APP_FOLDER}"'/;:a;n;ba;q}' -e '$aCategories='"${GNOME_APP_FOLDER}" {} \;
            fi
            if [ -d "${N7_APP_DIR}" ] ; then
                # shellcheck disable=SC2016
                find "${N7_APP_DIR}" -type f -name '*.desktop' \
                    -exec sed -i -e '/^\(Categories=\).*/{s//\1'"${GNOME_APP_FOLDER}"'/;:a;n;ba;q}' -e '$aCategories='"${GNOME_APP_FOLDER}" {} \;
            fi
        fi
    fi

    if command -v update-desktop-database >/dev/null 2>&1 ; then
        update-desktop-database "${APP_DIR}"
    fi

else
    echo
    echo "${N7_LAUNCHER_LINK} => \"${N7_LAUNCHER_SCRIPT}\""
    echo "${CFG_LINK} => \"${CFG_SCRIPT}\""
    echo "${CSC_LINK} => \"${CSC_SCRIPT}\""
    echo "${ENB_LINK} => \"${N7_PROXY_SCRIPT}\""
    echo
    wait_for_response "$(cat <<EOF
You do not appear to be running a freedesktop.org-compliant desktop environment.
No shortcuts or links will be created or managed; the links that would have been created are listed above for reference.
EOF
)"
    echo
fi

banner 'INSTALL COMPLETE!'

banner 'POST-INSTALL STEPS'

out "Launching Net-7 Launcher to perform updates"
N7_LAUNCHER_WINETRICKS_VERB="$(mktemp /tmp/n7launcher_XXXXXXXXXX.verb)"
add_exit_cmd "rm -f ${N7_LAUNCHER_WINETRICKS_VERB}"
N7_LAUNCHER_WINETRICKS_VERB_NAME="$(pf basename "${N7_LAUNCHER_WINETRICKS_VERB}" | sed 's/.verb//')"
cat <<EOF > "${N7_LAUNCHER_WINETRICKS_VERB}"
w_metadata ${N7_LAUNCHER_WINETRICKS_VERB_NAME} apps title="Net-7 Unified Installer"

load_${N7_LAUNCHER_WINETRICKS_VERB_NAME}()
{
    w_ahk_do "
        WinWaitActivate(WaitTitle, WaitText:=\"\", WaitTimeout:=2)
        {
            Loop
            {
                WinWaitActive, %WaitTitle%, %WaitText%, %WaitTimeout%
                If ErrorLevel
                {
                    WinActivate, %WaitTitle%, %WaitText%
                }
                Else
                {
                    Break
                }
            }
        }

        SetWinDelay 1000            ; wait extra second after each winwait
        SetTitleMatchMode, 2
        Run \"${N7_LAUNCHER_SCRIPT}\"
        Loop
        {
            updates_complete = 0
            Loop
            {
                WinWaitActive, Update available ahk_exe ${N7_LAUNCHER_EXE}, Version cannot be determined., 1
                If ErrorLevel
                {
                    updates_complete += 1
                    If updates_complete >= 5
                        Break
                    WinActivate, Update available ahk_exe ${N7_LAUNCHER_EXE}, Version cannot be determined.
                }
                else
                {
                    Break
                }
            }

            If updates_complete = 5
            {
                WinWaitActivate(\"LaunchNet7 v ahk_exe ${N7_LAUNCHER_EXE}\", \"Please select a server and hit play.\")
                ; Edit1 - ${N7_SERVER_HOSTNAME}
                ; WindowsForms10.BUTTON.app.0.2004eee10 - &Check
                ; WindowsForms10.BUTTON.app.0.2004eee12 - &Play
                ; WindowsForms10.BUTTON.app.0.2004eee13 - &Browse
                ; WindowsForms10.BUTTON.app.0.2004eee1 - Debug Launch
                ; WindowsForms10.BUTTON.app.0.2004eee2 - Local Cert
                ; WindowsForms10.BUTTON.app.0.2004eee4 - Lock Port
                ; WindowsForms10.BUTTON.app.0.2004eee6 - Delete TH6 Files
                ; WindowsForms10.BUTTON.app.0.2004eee7 - Disable Mouse Lock
                ; WindowsForms10.BUTTON.app.0.2004eee8 - Prototype Reorder
                ; WindowsForms10.BUTTON.app.0.2004eee9 - Packet Optimization
                ; WindowsForms10.COMBOBOX.app.0.2004eee1 - Local IP
                ; WindowsForms10.EDIT.app.0.2004eee2 - Client (uneditable)
                ; WindowsForms10.Window.8.app.0.2004eee3 - Host (uneditable)
                ControlClick, WindowsForms10.BUTTON.app.0.2004eee11 ; &Cancel (Ampersand seems to cause problems with AHK)
                Break
            }
            
            ; ? - Details >>
            ; ? - Skip
            ; WindowsForms10.BUTTON.app.0.2004eee4 - &Cancel
            ControlClick, WindowsForms10.BUTTON.app.0.2004eee5 ; &Update (Ampersand seems to cause problems with AHK)
            WinWaitActivate(\"LaunchNet7 - Information ahk_exe ${N7_LAUNCHER_EXE}\", \"Do you want to view the update report?\")
            ControlClick, Cancel       ; Button1 (OK - Button2)
        }
    "
}
EOF
output="$(WINEPREFIX=${WINEPREFIX} WINE="${WINE_EXEC}" ${WINETRICKS_EXEC} "${N7_LAUNCHER_WINETRICKS_VERB}" 2>&1)" || {
    rc="${?}"; err "rc: ${rc}, output: ${output}"; exit "${rc}"
}
echo

out "Run Net-7 Configuration"
out "1. Select your desired graphics settings and resolution"
out "2. Press 'Test Performance...' and wait for the test to complete"
out "3. Move the Net-7 Configuration window as the results tend to pop-up under it"
out "4. Press 'OK' on the Information window showing the results"
out "5. Press 'Apply' in the Net-7 Configuration window"
out "6. Press 'OK' in the Net-7 Configuration window"
WAIT=1 "${CFG_SCRIPT}"
echo

if [ -e "${ENB_LINUX_INSTALL_SOURCE}" ] ; then
    if prompt_for_yes "Cleanup downloaded and installer files from '${ENB_LINUX_INSTALL_SOURCE}'" ; then
        if ! rm --interactive=never --recursive "${ENB_LINUX_INSTALL_SOURCE}" ; then
            error_exit "There was an error while trying to remove '${ENB_LINUX_INSTALL_SOURCE}'"
        fi
    fi
fi
echo

if prompt_for_yes "Run the Character and Starship Creator to create a character" ; then
    WAIT=1 "${CSC_LINK}"
    if [ -d "${CSC_LINUX_AVATAR_PATH}" ] ; then
        out "Your Character and Starship Creator avatars are located here:\n"
        find "${CSC_LINUX_AVATAR_PATH}" -type f
        cat <<EOF

If you don't already have a character in the same slot, you don't need to do anything special, otherwise see:
https://forum.enb-emulator.com/index.php?/topic/6778-how-do-i-get-the-new-classes/&do=findComment&comment=87200

Most of this has been handled for you, but for reference:
https://forum.enb-emulator.com/index.php?/topic/7262-how-to-create-the-3-new-classes-npc-skins/
EOF
    fi
fi
echo

if prompt_for_yes "Register for an Earth & Beyond Emulator forum account (this is a prereq to creating a game account)" ; then
    xdg-open "https://forum.enb-emulator.com/index.php?/register/"
fi
echo

if prompt_for_yes "Register for an Earth & Beyond Emulator game account" ; then
    xdg-open "https://www.net-7.org/?#login"
fi
echo

if prompt_for_yes "Start Earth & Beyond Emulator" ; then
    "${N7_PROXY_SCRIPT}"
fi
echo
