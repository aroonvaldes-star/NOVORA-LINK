import React, { useState } from 'react';

interface NovoraLogoProps {
  variant?: 'banner' | 'icon' | 'horizontal';
  className?: string;
  theme?: 'dark' | 'light';
}

export const NovoraLogo: React.FC<NovoraLogoProps> = ({
  variant = 'horizontal',
  className = '',
  theme = 'dark',
}) => {
  const [imageError, setImageError] = useState(false);

  // User-provided URL from HTML reference
  const remoteBannerUrl =
    'https://lh3.googleusercontent.com/aida-public/AB6AXuC3RXz7B8dM5lY5_NfvVBMCMEs4JC5iGcGcHG3Q2bS7yiKu4ddCyWD23TFkChAfUfzi3Y5pagu_VLt2b8JYrJo6JtLBXu1SfBpInayFDDD6sXrmLQdeBlsqSelAVxyEW0az_YseM8kUg-51hcaIiF35w2vXQSU4PPNLbu9kipV0UhU1nc6xG1AvVX66_Z-r2hrXhkKZ4yAYh7p8R3gkxKjXKIVWKh9o2TNBB8liIxezOUhk62Sx4xb2jeEpKwt7UdA5WuA';

  if (variant === 'banner') {
    return (
      <div className={`relative w-full overflow-hidden flex items-center justify-center bg-surface-container-lowest border border-outline-variant ${className}`}>
        {!imageError ? (
          <img
            src={remoteBannerUrl}
            alt="NOVORA-LINK"
            className="w-full h-full object-contain max-h-[170px] transition-transform duration-700 hover:scale-105"
            referrerPolicy="no-referrer"
            onError={() => setImageError(true)}
          />
        ) : (
          <div className="w-full py-8 px-4 flex flex-col items-center justify-center bg-surface-container-low border border-outline-variant">
            {/* Brand emblem: kept as the NOVORA mark, not a themed UI color. */}
            <div className="relative w-16 h-16 mb-2">
              <svg viewBox="0 0 100 100" className="w-full h-full">
                <defs>
                  <linearGradient id="metalGrad" x1="0%" y1="0%" x2="100%" y2="100%">
                    <stop offset="0%" stopColor="#ffffff" />
                    <stop offset="50%" stopColor="#cfd8dc" />
                    <stop offset="100%" stopColor="#78909c" />
                  </linearGradient>
                  <linearGradient id="blueGrad" x1="0%" y1="0%" x2="100%" y2="100%">
                    <stop offset="0%" stopColor="#4361ee" />
                    <stop offset="50%" stopColor="#3a0ca3" />
                    <stop offset="100%" stopColor="#1e1035" />
                  </linearGradient>
                </defs>
                {/* Outer Silver Isometric Chevron */}
                <path
                  d="M50 15 L78 43 L64 57 L50 43 L36 57 L22 43 Z"
                  fill="url(#metalGrad)"
                  stroke="#eceff1"
                  strokeWidth="1.5"
                />
                <path
                  d="M22 43 L36 57 L24 69 L10 55 Z"
                  fill="#90a4ae"
                />
                <path
                  d="M78 43 L90 55 L76 69 L64 57 Z"
                  fill="#546e7a"
                />
                {/* Inner Cobalt Bar */}
                <path
                  d="M50 40 L62 52 L50 82 L38 70 Z"
                  fill="url(#blueGrad)"
                  stroke="#4cc9f0"
                  strokeWidth="1.5"
                />
              </svg>
            </div>
            <div className="text-center">
              <span className="text-[10px] tracking-[0.3em] font-label uppercase text-on-surface-variant font-bold">BIENVENIDO A</span>
              <h2 className="text-2xl font-bold tracking-[0.25em] text-on-surface font-headline mt-0.5 flex items-center justify-center gap-1">
                NOVORA<span className="text-primary">-</span>LINK
              </h2>
            </div>
          </div>
        )}
      </div>
    );
  }

  if (variant === 'icon') {
    return (
      <div className={`relative flex items-center justify-center ${className}`}>
        <svg viewBox="0 0 100 100" className="w-full h-full">
          <defs>
            <linearGradient id="metalGradIcon" x1="0%" y1="0%" x2="100%" y2="100%">
              <stop offset="0%" stopColor="#ffffff" />
              <stop offset="70%" stopColor="#90a4ae" />
            </linearGradient>
            <linearGradient id="blueGradIcon" x1="0%" y1="0%" x2="100%" y2="100%">
              <stop offset="0%" stopColor="#38bdf8" />
              <stop offset="100%" stopColor="#2563eb" />
            </linearGradient>
          </defs>
          <path
            d="M50 16 L80 46 L65 61 L50 46 L35 61 L20 46 Z"
            fill="url(#metalGradIcon)"
            stroke="#b0bec5"
            strokeWidth="2"
          />
          <path
            d="M50 42 L64 56 L52 82 L38 68 Z"
            fill="url(#blueGradIcon)"
          />
        </svg>
      </div>
    );
  }

  // Horizontal Wordmark variant: the glyph takes the UI's neutral and primary roles.
  return (
    <div className={`flex items-center gap-2 select-none ${className}`}>
      <div className="w-6 h-6 shrink-0">
        <svg viewBox="0 0 100 100" className="w-full h-full">
          <path
            d="M50 16 L80 46 L65 61 L50 46 L35 61 L20 46 Z"
            style={{ fill: 'var(--color-on-surface-variant)' }}
          />
          <path
            d="M50 42 L64 56 L52 82 L38 68 Z"
            style={{ fill: 'var(--color-primary)' }}
          />
        </svg>
      </div>
      <div className="flex items-baseline">
        <span className="font-headline font-bold text-sm tracking-[0.18em] text-on-surface">NOVORA</span>
        <span className="font-headline font-semibold text-xs tracking-wider text-primary ml-1">-LINK</span>
      </div>
    </div>
  );
};
