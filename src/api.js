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
  library: (q = '') => request(`/api/library?q=${encodeURIComponent(q)}`),
  favorites: () => request('/api/favorites'),
  folders: () => request('/api/folders'),
  addFolder: (path) => request('/api/folders', { method: 'POST', body: JSON.stringify({ path }) }),
  removeFolder: (path) => request(`/api/folders/${encodeURIComponent(path)}`, { method: 'DELETE' }),
  scan: () => request('/api/scan', { method: 'POST', body: '{}' }),
  favorite: (songId, favorite) => request('/api/favorites', { method: 'POST', body: JSON.stringify({ song_id: songId, favorite }) }),
  playlists: () => request('/api/playlists'),
  playlist: (id) => request(`/api/playlists/${id}`),
  createPlaylist: (name) => request('/api/playlists', { method: 'POST', body: JSON.stringify({ name }) }),
  deletePlaylist: (id) => request(`/api/playlists/${id}`, { method: 'DELETE' }),
  addPlaylistSong: (id, songId) => request(`/api/playlists/${id}/songs`, { method: 'POST', body: JSON.stringify({ song_id: songId }) }),
  removePlaylistSong: (id, songId) => request(`/api/playlists/${id}/songs/${songId}`, { method: 'DELETE' }),
  settings: () => request('/api/settings'),
  patchSettings: (patch) => request('/api/settings', { method: 'PATCH', body: JSON.stringify(patch) }),
  queue: () => request('/api/queue'),
  setQueue: (songIds) => request('/api/queue', { method: 'PUT', body: JSON.stringify({ song_ids: songIds }) }),
  history: (songId) => request('/api/history', { method: 'POST', body: JSON.stringify({ song_id: songId }) }),
}
