import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter, Route, Routes } from 'react-router-dom'
import './index.css'
import { Layout } from './Layout'
import { HomePage } from './pages/HomePage'
import { LobbyPage } from './pages/LobbyPage'
import { ReplayPage } from './pages/ReplayPage'

// Yollar (gateway bilinmeyen uzantısız yolu index.html'e çevirir, DESIGN.md §8.17):
//   /          lobi aç
//   /l/:code   davet linki: katıl ya da (token varsa) lobiye dön
//   /m/:id     tekrar izleme (token istemez)
createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <BrowserRouter>
      <Routes>
        <Route element={<Layout />}>
          <Route path="/" element={<HomePage />} />
          <Route path="/l/:code" element={<LobbyPage />} />
          <Route path="/m/:id" element={<ReplayPage />} />
          <Route path="*" element={<p>Sayfa bulunamadı.</p>} />
        </Route>
      </Routes>
    </BrowserRouter>
  </StrictMode>,
)
