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

export const api = {
  health: () => request('/api/health'),
  library: (q = '', includeHidden = false) => request(`/api/library?q=${encodeURIComponent(q)}&hidden=${includeHidden ? 1 : 0}`),
  favorites: () => request('/api/favorites'),
  historyList: (limit = 100) => request(`/api/history?limit=${limit}`),
  stats: () => request('/api/stats'),
  folders: () => request('/api/folders'),
  systemRoots: () => request('/api/system-roots'),
  addFolder: (path) => request('/api/folders', { method: 'POST', body: JSON.stringify({ path }) }),
  removeFolder: (path) => request(`/api/folders/${encodeURIComponent(path)}`, { method: 'DELETE' }),
  scan: () => request('/api/scan', { method: 'POST', body: '{}' }),
  scanSystem: () => request('/api/scan-system', { method: 'POST', body: '{}' }),
  favorite: (songId, favorite) => request('/api/favorites', { method: 'POST', body: JSON.stringify({ song_id: songId, favorite }) }),
  hideSong: (songId, hidden) => request('/api/hide', { method: 'POST', body: JSON.stringify({ song_id: songId, hidden }) }),
  playlists: (includeHidden = false) => request(`/api/playlists?hidden=${includeHidden ? 1 : 0}`),
  playlist: (id) => request(`/api/playlists/${id}`),
  createPlaylist: (name, folderId = null) => request('/api/playlists', { method: 'POST', body: JSON.stringify({ name, folder_id: folderId }) }),
  patchPlaylist: (id, patch) => request(`/api/playlists/${id}`, { method: 'PATCH', body: JSON.stringify(patch) }),
  deletePlaylist: (id) => request(`/api/playlists/${id}`, { method: 'DELETE' }),
  addPlaylistSong: (id, songId) => request(`/api/playlists/${id}/songs`, { method: 'POST', body: JSON.stringify({ song_id: songId }) }),
  removePlaylistSong: (id, songId) => request(`/api/playlists/${id}/songs/${songId}`, { method: 'DELETE' }),
  playlistFolders: () => request('/api/playlist-folders'),
  createPlaylistFolder: (name, parentId = null) => request('/api/playlist-folders', { method: 'POST', body: JSON.stringify({ name, parent_id: parentId }) }),
  patchPlaylistFolder: (id, patch) => request(`/api/playlist-folders/${id}`, { method: 'PATCH', body: JSON.stringify(patch) }),
  deletePlaylistFolder: (id) => request(`/api/playlist-folders/${id}`, { method: 'DELETE' }),
  importM3u8: (path, name) => request('/api/import-m3u8', { method: 'POST', body: JSON.stringify({ path, name }) }),
  exportM3u8: (playlistId, path) => request('/api/export-m3u8', { method: 'POST', body: JSON.stringify({ playlist_id: playlistId, path }) }),
  settings: () => request('/api/settings'),
  patchSettings: (patch) => request('/api/settings', { method: 'PATCH', body: JSON.stringify(patch) }),
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
