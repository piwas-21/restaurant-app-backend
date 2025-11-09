import { redirect } from 'next/navigation';

export default function MyOrdersPage() {
  // Redirect to the new /orders page
  redirect('/orders');
}
