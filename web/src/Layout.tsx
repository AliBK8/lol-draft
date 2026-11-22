import { Link, Outlet } from 'react-router-dom'

export function Layout() {
  return (
    <div className="app">
      <header className="top">
        <div className="top-inner">
          <Link to="/" className="brand">
            <span className="brand-mark" aria-hidden="true">
              LD
            </span>
            <span className="brand-name">
              LoL <span>Draft</span>
            </span>
          </Link>
        </div>
      </header>
      <main>
        <Outlet />
      </main>
      {/* Riot'un fan projesi politikası ("Legal Jibber Jabber") bu notu ister: şampiyon adları ve portreleri Riot'un. */}
      <footer className="legal">
        LoL Draft, Riot Games tarafından onaylanmamıştır; Riot Games'in ya da League of Legends'ın üretiminde veya
        yönetiminde resmî olarak yer alan kimsenin görüşlerini yansıtmaz. League of Legends ve Riot Games, Riot Games,
        Inc.'in ticari markalarıdır. Kâr amacı yoktur.
      </footer>
    </div>
  )
}
