export const ACCENTS = [
  { id:'orange', label:'orange', color:'#FF7A1A' },
  { id:'green', label:'green', color:'#45D483' },
  { id:'red', label:'red', color:'#FF5364' },
  { id:'blue', label:'blue', color:'#5B8CFF' },
  { id:'custard', label:'custard', color:'#E8C978' },
  { id:'purple', label:'purple', color:'#B586FF' },
  { id:'cyan', label:'cyan', color:'#42D9E8' },
  { id:'pink', label:'pink', color:'#FF6FAE' },
  { id:'indigo', label:'indigo', color:'#7C83FF' },
  { id:'teal', label:'teal', color:'#35C6A5' },
  { id:'gold', label:'gold', color:'#FFB84D' },
]

export const FONT_OPTIONS = [
  { id:'vazirmatn', name:'Vazirmatn', css:'"Vazirmatn Variable", "Vazirmatn", sans-serif' },
  { id:'segoe', name:'Segoe UI', css:'"Segoe UI Variable", "Segoe UI", sans-serif' },
  { id:'tahoma', name:'Tahoma', css:'Tahoma, sans-serif' },
  { id:'arial', name:'Arial', css:'Arial, sans-serif' },
  { id:'system', name:'System', css:'system-ui, -apple-system, sans-serif' },
]

export const THEME_PRESETS = [
  { id:'studio', name:'Studio', rank:'CORE', description:'Spotify-inspired charcoal surfaces', preview:['#121212','#1f1f1f','#2a2a2a'] },
  { id:'midnight', name:'Midnight', rank:'PLUS', description:'Deep navy with quieter contrast', preview:['#081018','#101d29','#1a2b39'] },
  { id:'vinyl', name:'Vinyl', rank:'PLUS', description:'Warm analog black and walnut', preview:['#15110f','#241c18','#35261f'] },
  { id:'aurora', name:'Aurora', rank:'PRO', description:'Cool glass surfaces with ambient color', preview:['#091312','#112522','#193933'] },
  { id:'noir', name:'Noir', rank:'ULTRA', description:'Pure monochrome, artwork-first', preview:['#050505','#101010','#1c1c1c'] },
]

export function effectiveAccent(settings, profile) {
  if (profile?.accent) return profile.accent
  if (settings.accent === 'custom') return settings.customColor || '#FF7A1A'
  return ACCENTS.find(x => x.id === settings.accent)?.color || '#FF7A1A'
}

export function applyAppearance(settings, profile = null, language = 'en') {
  const root = document.documentElement
  const mode = settings.themeMode || 'dark'
  const accent = effectiveAccent(settings, profile)
  const font = FONT_OPTIONS.find(x => x.id === settings.fontFamily) || FONT_OPTIONS[0]
  root.dataset.mode = mode
  root.dataset.preset = settings.themePreset || 'studio'
  root.dataset.density = settings.interfaceDensity || 'comfortable'
  root.dataset.accent = settings.accent || 'orange'
  root.style.setProperty('--accent', accent)
  root.style.setProperty('--accent-rgb', hexToRgbTriplet(accent))
  root.style.setProperty('--font-family', font.css)
  root.style.setProperty('--font-scale', String(settings.fontScale || 1))
  root.dir = language === 'fa' ? 'rtl' : 'ltr'
  root.lang = language
}

export function hexToRgbTriplet(hex) {
  const normalized = String(hex || '#ff7a1a').replace('#','')
  const raw = normalized.length === 3 ? normalized.split('').map(x => x + x).join('') : normalized.padEnd(6,'0').slice(0,6)
  const n = Number.parseInt(raw, 16)
  if (!Number.isFinite(n)) return '255 122 26'
  return `${(n >> 16) & 255} ${(n >> 8) & 255} ${n & 255}`
}
