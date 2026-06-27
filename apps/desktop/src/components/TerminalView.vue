<script setup lang="ts">
/**
 * TerminalView — Individual xterm.js terminal renderer.
 *
 * Creates an xterm.js Terminal instance, attaches it to a container element,
 * and wires up the data flow between xterm and the Electron terminal backend.
 *
 * Features:
 * - Auto-fit to container size via ResizeObserver
 * - Web links addon for clickable URLs
 * - Theme adaptation from CSS variables
 * - Focus management
 * - Proper cleanup on unmount
 */
import { onMounted, onUnmounted, ref, watch } from 'vue'
import { Terminal } from '@xterm/xterm'
import { FitAddon } from '@xterm/addon-fit'
import { WebLinksAddon } from '@xterm/addon-web-links'
import '@xterm/xterm/css/xterm.css'
import { useTerminal } from '@/composables/useTerminal'

const props = defineProps<{
  /** Terminal instance ID from the backend */
  terminalId: string
}>()

const emit = defineEmits<{
  'exited': [terminalId: string]
}>()

const {
  attachTerminal,
  closeTerminal,
  fitTerminal,
  focusTerminal,
  getTerminal,
} = useTerminal()

const containerRef = ref<HTMLElement | null>(null)
let term: Terminal | null = null
let fitAddon: FitAddon | null = null
let resizeObserver: ResizeObserver | null = null

// ---- xterm.js options ----

function createTerminal(): Terminal {
  return new Terminal({
    cursorBlink: true,
    cursorStyle: 'bar',
    fontFamily: "'Cascadia Code', 'Fira Code', 'JetBrains Mono', 'Consolas', 'Monaco', monospace",
    fontSize: 13,
    lineHeight: 1.2,
    letterSpacing: 0,
    scrollback: 10000,
    allowProposedApi: true,
    macOptionIsMeta: true,
    rightClickSelectsWord: true,
    theme: undefined, // Will be set by attachTerminal
  })
}

// ---- Lifecycle ----

onMounted(() => {
  if (!containerRef.value) return

  // Create xterm.js Terminal instance
  term = createTerminal()
  fitAddon = new FitAddon()

  // Load addons
  term.loadAddon(fitAddon)
  term.loadAddon(new WebLinksAddon())

  // Attach to the terminal backend
  attachTerminal(props.terminalId, containerRef.value, term, fitAddon)

  // Set up ResizeObserver for auto-fitting
  resizeObserver = new ResizeObserver(() => {
    if (fitAddon && term) {
      try {
        fitAddon.fit()
      } catch {
        // Ignore fit errors during rapid resize
      }
    }
  })
  resizeObserver.observe(containerRef.value)

  // Focus the terminal after a short delay to ensure it's ready
  setTimeout(() => {
    focusTerminal(props.terminalId)
  }, 100)
})

onUnmounted(() => {
  // Clean up ResizeObserver
  if (resizeObserver) {
    resizeObserver.disconnect()
    resizeObserver = null
  }

  // When the TerminalView is unmounted (tab closed, detached, or page
  // navigation), fully close the terminal to prevent orphaned PTY processes.
  // closeTerminal() calls detachTerminal() internally and also destroys the
  // PTY process in the main process.
  const instance = getTerminal(props.terminalId)
  if (instance) {
    closeTerminal(props.terminalId)
  } else {
    // Instance already removed (e.g. closeTerminal was called by the panel),
    // just dispose the local xterm if it still exists.
    if (term) {
      try { term.dispose() } catch { /* ignore */ }
    }
  }

  term = null
  fitAddon = null
})

// ---- Watch for terminal exit ----

watch(
  () => getTerminal(props.terminalId)?.exited,
  (exited) => {
    if (exited) {
      emit('exited', props.terminalId)
    }
  },
)

// ---- Public methods (exposed via template ref) ----

defineExpose({
  fit: () => fitTerminal(props.terminalId),
  focus: () => focusTerminal(props.terminalId),
})
</script>

<template>
  <div class="terminal-view">
    <div ref="containerRef" class="terminal-container" />
  </div>
</template>

<style scoped>
.terminal-view {
  width: 100%;
  height: 100%;
  overflow: hidden;
  background: var(--bg-primary);
  padding: 4px 6px;
  box-sizing: border-box;
}

.terminal-container {
  width: 100%;
  height: 100%;
  overflow: hidden;
}

/* Override xterm.js default styles to match the app theme */
.terminal-view :deep(.xterm) {
  padding: 0;
}

.terminal-view :deep(.xterm-viewport) {
  background-color: var(--bg-primary) !important;
  overflow-y: auto !important;
}

.terminal-view :deep(.xterm-screen) {
  background-color: var(--bg-primary) !important;
}

.terminal-view :deep(.xterm-rows) {
  font-family: 'Cascadia Code', 'Fira Code', 'JetBrains Mono', 'Consolas', 'Monaco', monospace !important;
}

/* Thin scrollbar for terminal */
.terminal-view :deep(.xterm-viewport::-webkit-scrollbar) {
  width: 6px;
}

.terminal-view :deep(.xterm-viewport::-webkit-scrollbar-thumb) {
  background: rgba(125, 133, 144, 0.3);
  border-radius: 3px;
}

.terminal-view :deep(.xterm-viewport::-webkit-scrollbar-thumb:hover) {
  background: rgba(125, 133, 144, 0.5);
}

.terminal-view :deep(.xterm-viewport::-webkit-scrollbar-track) {
  background: transparent;
}
</style>
