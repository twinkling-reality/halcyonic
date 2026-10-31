/*
 * halcyonic-disclaim: runs one program as its own responsible process, so that neither it nor
 * anything it starts is given the macOS privacy permissions (TCC) of the app that started
 * Halcyonic. Usage: halcyonic-disclaim /absolute/path/to/program [arguments...]
 *
 * It starts the program with posix_spawn, asking the system to disclaim responsibility, keeps
 * every open file descriptor (the browser's DevTools pipes are fds 3 and 4), forwards TERM, INT and
 * HUP to it, and exits with its status: its exit code, or 128 plus the signal that ended it.
 */
#include <errno.h>
#include <signal.h>
#include <spawn.h>
#include <stdio.h>
#include <string.h>
#include <sys/wait.h>

extern char **environ;

/* In libSystem since macOS 10.14; the system ships no header for it. Nonzero disclaims. */
int responsibility_spawnattrs_setdisclaim(posix_spawnattr_t *attrs, int disclaim);

static volatile sig_atomic_t child = 0;

static void forward(int signal_number) {
  if (child > 0) kill((pid_t)child, signal_number);
}

int main(int argc, char *argv[]) {
  if (argc < 2 || argv[1][0] != '/') {
    fprintf(stderr, "usage: halcyonic-disclaim /absolute/path/to/program [arguments...]\n");
    return 64;
  }
  const int forwarded[] = {SIGTERM, SIGINT, SIGHUP};
  sigset_t blocked, previous, none;
  sigemptyset(&blocked);
  sigemptyset(&none);
  struct sigaction action;
  memset(&action, 0, sizeof action);
  action.sa_handler = forward;
  sigemptyset(&action.sa_mask);
  for (size_t i = 0; i < sizeof forwarded / sizeof forwarded[0]; i++) {
    sigaddset(&blocked, forwarded[i]);
    if (sigaction(forwarded[i], &action, NULL) != 0) return 71;
  }
  /* Held until the child's pid is known, so a signal that arrives during the start is forwarded. */
  if (sigprocmask(SIG_BLOCK, &blocked, &previous) != 0) return 71;

  posix_spawnattr_t attrs;
  if (posix_spawnattr_init(&attrs) != 0) return 71;
  /* The child starts with no signal blocked and every forwarded signal at its default action. */
  if (posix_spawnattr_setsigmask(&attrs, &none) != 0 ||
      posix_spawnattr_setsigdefault(&attrs, &blocked) != 0 ||
      posix_spawnattr_setflags(&attrs, POSIX_SPAWN_SETSIGMASK | POSIX_SPAWN_SETSIGDEF) != 0) {
    posix_spawnattr_destroy(&attrs);
    return 71;
  }
  if (responsibility_spawnattrs_setdisclaim(&attrs, 1) != 0) {
    fprintf(stderr, "halcyonic-disclaim: the system refused to disclaim\n");
    posix_spawnattr_destroy(&attrs);
    return 71;
  }
  pid_t pid;
  int error = posix_spawn(&pid, argv[1], NULL, &attrs, &argv[1], environ);
  posix_spawnattr_destroy(&attrs);
  if (error != 0) {
    fprintf(stderr, "halcyonic-disclaim: %s\n", strerror(error));
    return 71;
  }
  child = pid;
  if (sigprocmask(SIG_SETMASK, &previous, NULL) != 0) {
    kill(pid, SIGKILL);
    return 71;
  }
  int status;
  while (waitpid(pid, &status, 0) < 0) {
    if (errno != EINTR) return 71;
  }
  if (WIFEXITED(status)) return WEXITSTATUS(status);
  if (WIFSIGNALED(status)) return 128 + WTERMSIG(status);
  return 71;
}
