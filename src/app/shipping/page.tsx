import { Truck, RotateCcw, Shield, Package } from 'lucide-react'
import type { Metadata } from 'next'

export const metadata: Metadata = {
  title: 'משלוחים והחזרות | NEXOR Padel',
  description: 'מדיניות המשלוחים, החזרות וההחלפות של NEXOR Padel. משלוח חינם מעל ₪280, החזרות ב-30 יום.',
}

const deliveryOptions = [
  {
    name: 'משלוח סטנדרטי',
    time: '2–3 ימי עסקים',
    price: '₪29',
    note: 'מעקב מלא בכל שלב',
  },
  {
    name: 'משלוח אקספרס',
    time: 'יום המחרת',
    price: '₪59',
    note: 'הזמנה עד 14:00 — מגיע מחר',
  },
  {
    name: 'משלוח חינם',
    time: '2–3 ימי עסקים',
    price: 'חינם',
    note: 'בהזמנות מעל ₪280',
  },
]

const faqs = [
  {
    q: 'כמה זמן עד שהחבילה מגיעה?',
    a: 'משלוח סטנדרטי לוקח 2–3 ימי עסקים. משלוח אקספרס מגיע ליום המחרת כאשר ההזמנה בוצעה לפני 14:00.',
  },
  {
    q: 'האם ניתן לעקוב אחר ההזמנה?',
    a: 'בהחלט. תקבל מספר מעקב ב-SMS ואימייל מיד לאחר שהחבילה נשלחה.',
  },
  {
    q: 'מה קורה אם החבילה לא הגיעה?',
    a: 'אם החבילה לא הגיעה בתוך 5 ימי עסקים, פנה אלינו ואנחנו נטפל בעניין מיידית — כולל שיפוי מלא במידה ויידרש.',
  },
  {
    q: 'האם ניתן להחזיר מוצר שנרכש בסייל?',
    a: 'כן, מוצרים שנרכשו במחיר מבצע ניתנים להחזרה בתנאים רגילים בתוך 30 יום, כל עוד לא נעשה בהם שימוש.',
  },
]

export default function ShippingPage() {
  return (
    <div className="min-h-screen pt-20">
      {/* Hero */}
      <section className="relative py-24 overflow-hidden">
        <div className="absolute inset-0 bg-[#050505]" />
        <div className="absolute inset-0 bg-grid opacity-25" />
        <div
          className="absolute inset-0 opacity-12"
          style={{ background: 'radial-gradient(ellipse at 50% 0%, rgba(201,165,90,0.12) 0%, transparent 60%)' }}
        />

        <div className="relative max-w-7xl mx-auto px-5 sm:px-8 lg:px-10 text-center">
          <div className="section-label mx-auto inline-flex mb-6">מידע לרוכש</div>
          <h1 className="section-title mb-5">
            משלוחים<br />
            <span className="text-gold-gradient">והחזרות</span>
          </h1>
          <p className="text-[#f2eddf]/50 text-lg max-w-xl mx-auto leading-relaxed">
            אנחנו רוצים שהחוויה שלך תהיה חלקה — מהרכישה ועד קבלת הציוד.
          </p>
        </div>
      </section>

      <div className="divider" />

      <div className="max-w-5xl mx-auto px-5 sm:px-8 lg:px-10 py-20 space-y-16">

        {/* Delivery Options */}
        <section>
          <div className="flex items-center gap-4 mb-8">
            <div className="w-10 h-10 bg-[rgba(201,165,90,0.1)] border border-[rgba(201,165,90,0.2)] flex items-center justify-center flex-shrink-0">
              <Truck className="w-5 h-5" style={{ color: '#c9a55a' }} />
            </div>
            <h2 className="text-[#f2eddf] font-bold text-2xl">אפשרויות משלוח</h2>
          </div>

          <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
            {deliveryOptions.map((opt) => (
              <div
                key={opt.name}
                className="luxury-card p-6"
              >
                <div className="flex items-start justify-between mb-4">
                  <h3 className="text-[#f2eddf] font-semibold">{opt.name}</h3>
                  <span
                    className="font-bold text-lg ltr"
                    style={{ color: opt.price === 'חינם' ? '#c9a55a' : '#f2eddf' }}
                  >
                    {opt.price}
                  </span>
                </div>
                <p className="text-[#c9a55a] text-sm font-semibold mb-1">{opt.time}</p>
                <p className="text-[#f2eddf]/35 text-xs">{opt.note}</p>
              </div>
            ))}
          </div>
        </section>

        <div className="divider-subtle" />

        {/* Returns Policy */}
        <section>
          <div className="flex items-center gap-4 mb-8">
            <div className="w-10 h-10 bg-[rgba(201,165,90,0.1)] border border-[rgba(201,165,90,0.2)] flex items-center justify-center flex-shrink-0">
              <RotateCcw className="w-5 h-5" style={{ color: '#c9a55a' }} />
            </div>
            <h2 className="text-[#f2eddf] font-bold text-2xl">מדיניות החזרות</h2>
          </div>

          <div className="luxury-card p-8">
            <div className="grid grid-cols-1 md:grid-cols-2 gap-8">
              <div>
                <h3 className="text-[#c9a55a] font-bold uppercase tracking-widest text-xs mb-3">תנאי ההחזרה</h3>
                <ul className="space-y-3">
                  {[
                    '30 יום מיום הקנייה',
                    'המוצר חייב להיות ללא שימוש',
                    'אריזה מקורית שלמה',
                    'כל תגיות המחיר במקום',
                    'קבלה או אסמכתא על הרכישה',
                  ].map((item) => (
                    <li key={item} className="flex items-center gap-3 text-sm text-[#f2eddf]/60">
                      <span className="w-1.5 h-1.5 bg-[#c9a55a] flex-shrink-0" />
                      {item}
                    </li>
                  ))}
                </ul>
              </div>
              <div>
                <h3 className="text-[#c9a55a] font-bold uppercase tracking-widest text-xs mb-3">חריגים</h3>
                <ul className="space-y-3">
                  {[
                    'מחבטים שנעשה בהם שימוש',
                    'מוצרים שנפגעו לאחר קנייה',
                    'מוצרי היגיינה (גריפים בשימוש)',
                    'מוצרים שהוזמנו במיוחד',
                  ].map((item) => (
                    <li key={item} className="flex items-center gap-3 text-sm text-[#f2eddf]/40">
                      <span className="w-1.5 h-1.5 bg-[#f2eddf]/20 flex-shrink-0" />
                      {item}
                    </li>
                  ))}
                </ul>
              </div>
            </div>
          </div>
        </section>

        <div className="divider-subtle" />

        {/* Exchange Policy */}
        <section>
          <div className="flex items-center gap-4 mb-8">
            <div className="w-10 h-10 bg-[rgba(201,165,90,0.1)] border border-[rgba(201,165,90,0.2)] flex items-center justify-center flex-shrink-0">
              <Package className="w-5 h-5" style={{ color: '#c9a55a' }} />
            </div>
            <h2 className="text-[#f2eddf] font-bold text-2xl">מדיניות החלפות</h2>
          </div>

          <div className="luxury-card p-8">
            <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
              <div className="text-center p-4">
                <div className="text-4xl font-black text-gold-gradient mb-2 ltr">14</div>
                <p className="text-[#f2eddf]/50 text-sm">ימים להחלפה חינמית</p>
              </div>
              <div className="text-center p-4 border-x border-[rgba(201,165,90,0.08)]">
                <div className="text-4xl font-black text-gold-gradient mb-2">חינם</div>
                <p className="text-[#f2eddf]/50 text-sm">עלות ההחלפה בתוך 14 יום</p>
              </div>
              <div className="text-center p-4">
                <div className="text-4xl font-black text-gold-gradient mb-2">כל</div>
                <p className="text-[#f2eddf]/50 text-sm">המוצרים ניתנים להחלפה</p>
              </div>
            </div>
            <div className="divider-subtle my-6" />
            <p className="text-[#f2eddf]/40 text-sm leading-relaxed text-center">
              רוצה לעבור לדגם אחר? אין בעיה — ההחלפה חינמית בתוך 14 הימים הראשונים מיום הקנייה.
              לאחר 14 יום, תחויב בעלות משלוח בלבד.
            </p>
          </div>
        </section>

        <div className="divider-subtle" />

        {/* Shield / Guarantee */}
        <section>
          <div className="flex items-center gap-4 mb-8">
            <div className="w-10 h-10 bg-[rgba(201,165,90,0.1)] border border-[rgba(201,165,90,0.2)] flex items-center justify-center flex-shrink-0">
              <Shield className="w-5 h-5" style={{ color: '#c9a55a' }} />
            </div>
            <h2 className="text-[#f2eddf] font-bold text-2xl">שאלות נפוצות</h2>
          </div>

          <div className="space-y-3">
            {faqs.map((faq) => (
              <div key={faq.q} className="luxury-card p-6">
                <h3 className="text-[#f2eddf] font-semibold mb-2 text-sm">{faq.q}</h3>
                <p className="text-[#f2eddf]/45 text-sm leading-relaxed">{faq.a}</p>
              </div>
            ))}
          </div>
        </section>

        {/* CTA */}
        <section className="text-center py-8">
          <div className="divider mb-12" />
          <p className="text-[#f2eddf]/30 text-xs uppercase tracking-widest mb-6">
            שאלות נוספות?
          </p>
          <a href="/contact" className="btn-gold">
            דבר איתנו
          </a>
        </section>

      </div>
    </div>
  )
}
