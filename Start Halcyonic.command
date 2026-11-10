#!/bin/zsh -l
# Starts Halcyonic from Finder with a double-click, then opens the pairing page in the browser once
# it is ready (ADR 0047). A login shell, so the PATH that finds node and pnpm is the one Terminal
# gives. Close this window, or press Ctrl-C in it, to stop Halcyonic.
cd "${0:A:h}" || exit 1
if ! command -v pnpm >/dev/null 2>&1; then
  print "pnpm is not installed. See docs/internal/runbooks/LOCAL_DEVELOPMENT.md, Prerequisites."
  exit 1
fi
exec pnpm start --open-pairing-page
