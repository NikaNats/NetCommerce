/**
 * Checkout form contract, transcribed from the server — not guessed.
 *
 * Sources:
 *   src/Ordering/Ordering.Application/Orders/Commands/OrderCommands.cs
 *     (CreateOrderCommand, AddressDto)
 *   src/Ordering/Ordering.Domain/Orders/OrderAddresses.cs
 *     (ShippingAddress.Create required fields)
 *
 * The API stamps CustomerId from the JWT subject and the IdempotencyFilter
 * overwrites IdempotencyKey from the X-Idempotency-Key header, so neither is
 * trusted from the form — but both are still SENT (a zero-GUID placeholder and
 * the header key respectively) so the body matches the command shape instead
 * of relying on serializer defaults.
 */

import { isGuid } from '@/lib/validation/guid';

export interface CheckoutAddressInput {
  recipientName: string;
  street: string;
  city: string;
  state: string;
  postalCode: string;
  country: string;
  phone: string;
}

export interface CheckoutInput {
  customerName: string;
  customerEmail: string;
  shipping: CheckoutAddressInput;
  /** Omitted when billing matches shipping — the action copies it server-side. */
  billing?: CheckoutAddressInput;
  sameAsBilling: boolean;
  couponCode?: string;
}

export type CheckoutErrors = Partial<
  Record<'customerName' | 'customerEmail' | 'couponCode', string> &
    Record<`shipping.${keyof CheckoutAddressInput}` | `billing.${keyof CheckoutAddressInput}`, string>
>;

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

// Re-exported so checkout callers keep one import: the key-shape guard is part
// of this module's contract (a tampered idempotency key must fall back to a
// fresh one), while the pattern itself lives in lib/validation/guid.ts.
export { isGuid };

function checkAddress(
  prefix: 'shipping' | 'billing',
  address: CheckoutAddressInput,
  errors: CheckoutErrors,
): void {
  // Required set mirrors ShippingAddress.Create, which throws on a blank
  // recipientName/street/city/country (an ArgumentException would otherwise
  // surface as a 500 instead of a field error).
  if (!address.recipientName.trim()) errors[`${prefix}.recipientName`] = 'Recipient name is required.';
  if (!address.street.trim()) errors[`${prefix}.street`] = 'Street is required.';
  if (!address.city.trim()) errors[`${prefix}.city`] = 'City is required.';
  if (!address.country.trim()) errors[`${prefix}.country`] = 'Country is required.';
}

/**
 * Validate a checkout submission before any network call.
 *
 * Runs in the server action (authoritative) and shapes the client form's
 * required attributes — the two must agree, or the browser blocks what the
 * server allows and vice versa.
 */
export function validateCheckoutInput(input: CheckoutInput): CheckoutErrors {
  const errors: CheckoutErrors = {};

  if (!input.customerName.trim()) {
    errors.customerName = 'Your name is required.';
  }

  if (!EMAIL_PATTERN.test(input.customerEmail.trim())) {
    errors.customerEmail = 'Enter a valid email address.';
  }

  checkAddress('shipping', input.shipping, errors);

  if (!input.sameAsBilling && input.billing) {
    checkAddress('billing', input.billing, errors);
  }

  return errors;
}

export function hasCheckoutErrors(errors: CheckoutErrors): boolean {
  return Object.keys(errors).length > 0;
}
