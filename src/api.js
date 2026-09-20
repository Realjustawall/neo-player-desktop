async function request(path, options = {}) {
  const response = await fetch(path, {
    headers: { 'Content-Type': 'application/json', ...(options.headers || {}) },
    ...options,
  })
  if (!response.ok) {
    let message = `Request failed (${response.status})`
    try { message = (await response.json()).error || message } catch {}
    throw new Error(message)
  }
  if (response.status === 204) return null
  return response.json()
}

function nativeApi() {
  return window.pywebview?.api || null
}

async function nativeFirst(name, args, fallback) {
  const fn = nativeApi()?.[name]
  if (typeof fn === 'function') return fn(...args)
  return fallback()
}

export const api = {
  health: () => request('/api/health'),
  nativeCapabilities: () => nativeFirst('native_capabilities', [], () => Promise.resolve({ nativeCore: false })),
  scanStatus: () => nativeFirst('scan_status', [], () => Promise.resolve({ running: false, phase: 'idle' })),
  autoScanMusic: () => nativeFirst('auto_scan_music', [], () => request('/api/scan', { method: 'POST', body: '{}' })),
  pickAndScanFolder: () => nativeFirst('pick_and_scan_folder', [], async () => ({ cancelled: true, native: false })),
  library: (q = '', includeHidden = false) => nativeFirst('native_library', [q, includeHidden], () => request(`/api/library?q=${encodeURIComponent(q)}&hidden=${includeHidden ? 1 : 0}`)),
  favorites: () => nativeFirst('native_favorites', [], () => request('/api/favorites')),
  historyList: (limit = 100) => nativeFirst('native_history', [limit], () => request(`/api/history?limit=${limit}`)),
  stats: () => nativeFirst('native_stats', [], () => request('/api/stats')),
  recentSearches: (limit = 8) => request(`/api/recent-searches?limit=${limit}`),
  addRecentSearch: (query) => request('/api/recent-searches', { method: 'POST', body: JSON.stringify({ query }) }),
  clearRecentSearches: () => request('/api/recent-searches', { method: 'DELETE' }),
  cacheStatus: () => request('/api/cache'),
  clearCache: () => request('/api/cache/clear', { method: 'POST', body: '{}' }),
  folders: () => nativeFirst('native_folders', [], () => request('/api/folders')),
  systemRoots: () => nativeFirst('native_system_roots', [], () => request('/api/system-roots')),
  addFolder: (path) => nativeFirst('native_add_folder', [path], () => request('/api/folders', { method: 'POST', body: JSON.stringify({ path }) })),
  removeFolder: (path) => nativeFirst('native_remove_folder', [path], () => request(`/api/folders/${encodeURIComponent(path)}`, { method: 'DELETE' })),
  scan: () => nativeFirst('scan_library', [false], () => request('/api/scan', { method: 'POST', body: '{}' })),
  scanSystem: () => nativeFirst('scan_library', [true], () => request('/api/scan-system', { method: 'POST', body: '{}' })),
  addStream: (url, title = '', artist = '') => request('/api/streams', { method: 'POST', body: JSON.stringify({ url, title, artist }) }),
  favorite: (songId, favorite) => request('/api/favorites', { method: 'POST', body: JSON.stringify({ song_id: songId, favorite }) }),
  hideSong: (songId, hidden) => request('/api/hide', { method: 'POST', body: JSON.stringify({ song_id: songId, hidden }) }),
  pins: () => request('/api/pins'),
  setPin: (kind, itemKey, pinned) => request('/api/pins', { method: 'POST', body: JSON.stringify({ kind, item_key: itemKey, pinned }) }),
  playlists: (includeHidden = false) => request(`/api/playlists?hidden=${includeHidden ? 1 : 0}`),
  playlist: (id) => request(`/api/playlists/${id}`),
  createPlaylist: (name, folderId = null) => request('/api/playlists', { method: 'POST', body: JSON.stringify({ name, folder_id: folderId }) }),
  patchPlaylist: (id, patch) => request(`/api/playlists/${id}`, { method: 'PATCH', body: JSON.stringify(patch) }),
  deletePlaylist: (id) => request(`/api/playlists/${id}`, { method: 'DELETE' }),
  addPlaylistSong: (id, songId) => request(`/api/playlists/${id}/songs`, { method: 'POST', body: JSON.stringify({ song_id: songId }) }),
  removePlaylistSong: (id, songId) => request(`/api/playlists/${id}/songs/${songId}`, { method: 'DELETE' }),
  reorderPlaylist: (id, songIds) => request(`/api/playlists/${id}/reorder`, { method: 'PUT', body: JSON.stringify({ song_ids: songIds }) }),
  playlistFolders: () => request('/api/playlist-folders'),
  createPlaylistFolder: (name, parentId = null) => request('/api/playlist-folders', { method: 'POST', body: JSON.stringify({ name, parent_id: parentId }) }),
  patchPlaylistFolder: (id, patch) => request(`/api/playlist-folders/${id}`, { method: 'PATCH', body: JSON.stringify(patch) }),
  deletePlaylistFolder: (id) => request(`/api/playlist-folders/${id}`, { method: 'DELETE' }),
  importM3u8: (path, name) => request('/api/import-m3u8', { method: 'POST', body: JSON.stringify({ path, name }) }),
  exportM3u8: (playlistId, path) => request('/api/export-m3u8', { method: 'POST', body: JSON.stringify({ playlist_id: playlistId, path }) }),
  settings: () => nativeFirst('native_settings', [], () => request('/api/settings')),
  patchSettings: (patch) => nativeFirst('native_patch_settings', [patch], () => request('/api/settings', { method: 'PATCH', body: JSON.stringify(patch) })),
  queue: () => request('/api/queue'),
  setQueue: (songIds) => request('/api/queue', { method: 'PUT', body: JSON.stringify({ song_ids: songIds }) }),
  history: (songId, skipped = false) => request('/api/history', { method: 'POST', body: JSON.stringify({ song_id: songId, skipped }) }),
  lyrics: (songId) => request(`/api/lyrics/${songId}`),
  saveLyrics: (songId, payload) => request(`/api/lyrics/${songId}`, { method: 'POST', body: JSON.stringify(payload) }),
  profile: (songId) => request(`/api/profiles/${songId}`),
  saveProfile: (songId, payload) => request(`/api/profiles/${songId}`, { method: 'POST', body: JSON.stringify(payload) }),
  smartMix: (seedId = null, options = {}) => {
    const p = new URLSearchParams({ limit: String(options.limit || 50) })
    if (seedId) p.set('seed', String(seedId))
    if (options.genre) p.set('genre', options.genre)
    if (options.mood) p.set('mood', options.mood)
    return request(`/api/smart-mix?${p}`)
  },
  backup: () => request('/api/backup'),
  restore: (data) => request('/api/restore', { method: 'POST', body: JSON.stringify(data) }),
}
