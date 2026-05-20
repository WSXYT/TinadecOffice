<script setup lang="ts">
import ChatHeader from './ChatHeader.vue'
import MessageList from './MessageList.vue'
import ComposerBar from './ComposerBar.vue'
import WelcomeScreen from './WelcomeScreen.vue'
import TaskGraphPanel from './TaskGraphPanel.vue'
import type { MessageDto, SessionDto, ProjectDto, OrchestrationSnapshotDto } from '../api'
import type { AgentMode, PermissionLevel } from '@/types/mode'

const props = defineProps<{
  messages: MessageDto[]
  sessions: SessionDto[]
  projects: ProjectDto[]
  currentSession: SessionDto | null
  currentProject: ProjectDto | null
  selectedProjectId: string | null
  modelName: string
  orchestration: OrchestrationSnapshotDto | null
  busy: boolean
  draft: string
  mode: AgentMode
  permission: PermissionLevel
}>()

const emit = defineEmits<{
  'update:draft': [value: string]
  'update:mode': [value: AgentMode]
  'update:permission': [value: PermissionLevel]
  'send': []
  'welcome-send': [content: string]
  'create-project': []
  'select-project': [id: string]
}>()
</script>

<template>
  <section class="conversation">
    <template v-if="messages.length === 0">
      <WelcomeScreen
        :projects="props.projects"
        :selected-project-id="selectedProjectId"
        :model-name="modelName"
        :busy="busy"
        @send="emit('welcome-send', $event)"
        @create-project="emit('create-project')"
        @select-project="emit('select-project', $event)"
        @update:mode="emit('update:mode', $event)"
        @update:permission="emit('update:permission', $event)"
      />
    </template>
    <template v-else>
      <ChatHeader :current-session="currentSession" :current-project="currentProject" />
      <TaskGraphPanel :snapshot="orchestration" />
      <MessageList :messages="messages" />
      <ComposerBar
        :busy="busy"
        :model-value="draft"
        :mode="mode"
        :permission="permission"
        @update:model-value="emit('update:draft', $event)"
        @update:mode="emit('update:mode', $event)"
        @update:permission="emit('update:permission', $event)"
        @submit="emit('send')"
      />
    </template>
  </section>
</template>
