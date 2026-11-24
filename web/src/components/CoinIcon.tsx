/**
 * Altın işareti (§8.40): Lucide "coins" ikonu (lucide-static 1.79.0, ISC lisansı, © Lucide Icons and Contributors;
 * https://lucide.dev/icons/coins). Çizgi stili, rengi yanındaki yazının rengi (currentColor), boyu yazıyla ölçeklenir.
 * Renksiz bilerek: kadroda 10 kez tekrar eder. Süs: ekran okuyucuya gizli, anlamı yanındaki sayı ve ipucu taşır.
 */
export function CoinIcon() {
  return (
    <svg
      className="coin"
      viewBox="0 0 24 24"
      width="1.1em"
      height="1.1em"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      focusable="false"
    >
      <path d="M13.744 17.736a6 6 0 1 1-7.48-7.48" />
      <path d="M15 6h1v4" />
      <path d="m6.134 14.768.866-.5 2 3.464" />
      <circle cx="16" cy="8" r="6" />
    </svg>
  )
}
