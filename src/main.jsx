import React from 'react'
import ReactDOM from 'react-dom/client'
import '@fontsource-variable/vazirmatn/wght.css'
import PlayerApp from './PlayerApp'
import FeatureHub from './FeatureHub'
import ImmersiveHub from './ImmersiveHub'
import LogoRepair from './LogoRepair'
import LibraryBoost from './LibraryBoost'
import './styles.css'
import './enhancements.css'
import './featurehub.css'
import './polish.css'
import './immersive.css'
import './core-settings.css'
import './library-boost.css'

ReactDOM.createRoot(document.getElementById('root')).render(
  <React.StrictMode>
    <PlayerApp />
    <FeatureHub />
    <ImmersiveHub />
    <LogoRepair />
    <LibraryBoost />
  </React.StrictMode>,
)
