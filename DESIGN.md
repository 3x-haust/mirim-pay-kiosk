# MIRIM PAY Figma Contract

## Source of truth

The only user-visible runtime states are the four supplied Figma frames:

| Runtime state | Figma node | View |
| --- | --- | --- |
| Start | `229:667` | `MenuView` |
| Cart and barcode scanning | `229:927` | `CartView` |
| Payment selection | `229:763` | `PaymentView` |
| Payment success | `663:78` | `PaymentView` |

No catalog, category-filter, product-grid, intermediate barcode form, loading page,
or other user-visible screen may exist.

## State graph

```text
Menu start -> Cart -> Payment selection -> Payment success -> Menu start
                 ^             |
                 +-------------+
```

- `주문하기` opens the Cart frame directly.
- Barcode input remains an invisible scanner/automation surface inside `CartView`;
  it must not add visible controls absent from the Cart frame.
- Payment back returns to Cart.
- Success acknowledgement returns to Menu start.

## Visual system

- Logical canvas: `1080x1920`.
- Scaling: uniform portrait host.
- Colors, typography, radii, strokes, spacing, and component geometry are defined in
  `KioskProject/Resources/KioskFoundations.xaml` and the view-local measured tokens.
- Figma exports under `.omo/evidence/figma-reference/` are exact visual targets.
- Dynamic product names, prices, quantities, counts, and totals may differ; their
  containers, alignment, typography, and surrounding controls may not.

## Interaction constraints

- UI Automation IDs remain stable on live controls.
- Scanner input uses the Cart frame without displaying a non-Figma form.
- All event waits are subscribed before action and bounded; no sleeps or polling.
- The UI must remain real WPF controls and resources, never a pasted screenshot.
