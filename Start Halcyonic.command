#!/bin/zsh -l
# Starts Halcyonic from Finder with a double-click, then opens the pairing page in the browser once
# it is ready (ADR 0047). A login shell, so the PATH that finds node and pnpm is the one Terminal
# gives. Close this window, or press Ctrl-C in it, to stop Halcyonic.
# The setup page's Restart to Apply closes Halcyonic with exit code 75; this starts it again, with
# the settings as they are now, and opens a new pairing page. Any other exit ends here (ADR 0053).
cd "${0:A:h}" || exit 1
if ! command -v pnpm >/dev/null 2>&1; then
  print "pnpm is not installed. See docs/internal/runbooks/LOCAL_DEVELOPMENT.md, Prerequisites."
  exit 1
fi
while true; do
  pnpm start --open-pairing-page --restartable
  code=$?
  (( code == 75 )) || exit $code
  print "Starting Halcyonic again with the new settings."
done
