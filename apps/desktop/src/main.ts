import { createApp } from 'vue'
import App from './App.vue'
import router from './router'
import i18n from './i18n'
import { useTheme } from './composables/useTheme'
import './styles.css'

const app = createApp(App)
app.use(router)
app.use(i18n)

// 在挂载前初始化主题，确保 DOM 准备好后再应用样式
const { applyInitialTheme } = useTheme()
if (applyInitialTheme) {
  applyInitialTheme()
}

app.mount('#app')  
