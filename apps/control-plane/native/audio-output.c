/*
 * halcyonic-audio-output: says whether any of the given processes sends sound to an output now,
 * as CoreAudio itself records it, so Halcyonic can tell whether a game plays sound without reading
 * anything the game's page reports. Usage: halcyonic-audio-output <pid> [pid...]
 *
 * It reads the system's list of CoreAudio client processes (kAudioHardwarePropertyProcessObjectList),
 * each one's pid (kAudioProcessPropertyPID) and, for those given, whether it runs audio IO with at
 * least one active output stream (kAudioProcessPropertyIsRunningOutput). It prints 1 when one does,
 * else 0, and exits 0; 64 for arguments that are not pids; 70 when CoreAudio cannot be read.
 * It changes nothing and reads no sound.
 */
#include <CoreAudio/CoreAudio.h>
#include <errno.h>
#include <stdio.h>
#include <stdlib.h>

static int usage(void) {
  fprintf(stderr, "usage: halcyonic-audio-output <pid> [pid...]\n");
  return 64;
}

int main(int argc, char *argv[]) {
  if (argc < 2 || argc > 4097) return usage();
  pid_t wanted[4096];
  int count = 0;
  for (int i = 1; i < argc; i++) {
    char *end = NULL;
    errno = 0;
    long value = strtol(argv[i], &end, 10);
    if (errno != 0 || end == argv[i] || *end != '\0' || value < 1 || value > 99999999) return usage();
    wanted[count++] = (pid_t)value;
  }

  AudioObjectPropertyAddress list = {kAudioHardwarePropertyProcessObjectList,
                                     kAudioObjectPropertyScopeGlobal,
                                     kAudioObjectPropertyElementMain};
  UInt32 size = 0;
  if (AudioObjectGetPropertyDataSize(kAudioObjectSystemObject, &list, 0, NULL, &size) != noErr)
    return 70;
  UInt32 objects = size / (UInt32)sizeof(AudioObjectID);
  AudioObjectID *processes = calloc(objects == 0 ? 1 : objects, sizeof(AudioObjectID));
  if (processes == NULL) return 70;
  if (objects > 0 &&
      AudioObjectGetPropertyData(kAudioObjectSystemObject, &list, 0, NULL, &size, processes) != noErr) {
    free(processes);
    return 70;
  }
  objects = size / (UInt32)sizeof(AudioObjectID);

  int running = 0;
  for (UInt32 i = 0; i < objects && running == 0; i++) {
    AudioObjectPropertyAddress pidAddress = {kAudioProcessPropertyPID, kAudioObjectPropertyScopeGlobal,
                                             kAudioObjectPropertyElementMain};
    pid_t pid = 0;
    UInt32 pidSize = sizeof pid;
    /* A client that ends between the list and this read is passed over. */
    if (AudioObjectGetPropertyData(processes[i], &pidAddress, 0, NULL, &pidSize, &pid) != noErr) continue;
    int asked = 0;
    for (int j = 0; j < count; j++)
      if (wanted[j] == pid) asked = 1;
    if (!asked) continue;
    AudioObjectPropertyAddress outputAddress = {kAudioProcessPropertyIsRunningOutput,
                                                kAudioObjectPropertyScopeGlobal,
                                                kAudioObjectPropertyElementMain};
    UInt32 output = 0;
    UInt32 outputSize = sizeof output;
    if (AudioObjectGetPropertyData(processes[i], &outputAddress, 0, NULL, &outputSize, &output) != noErr) {
      free(processes);
      return 70;
    }
    if (output != 0) running = 1;
  }
  free(processes);
  printf("%d\n", running);
  return 0;
}
