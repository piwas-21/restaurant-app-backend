# Admin Reservations Management - Improvements Plan

## Issues to Fix

### 1. Calendar View - Status Colors Not Showing
**Problem:** Calendar events not reflecting status colors
**Solution:** 
- Verify eventStyleGetter is properly applied
- Check if Calendar component is receiving the style function
- Ensure status colors are being set correctly for each event type

### 2. List View Card Styling Issues
**Problem:** Cards look cramped, fonts too small, no visual status indicators
**Solution:**
- Remove checkbox (keep selection via click only)
- Increase font sizes: 
  - Customer name: 1.1rem
  - Contact info: 0.875rem
  - Info items: 0.875rem
- Add status color accent to left border of cards (4px solid)
- Increase card padding to 1.25rem
- Better spacing between elements

### 3. Missing Reservation Status in Cards
**Problem:** Status badge not visible or missing
**Solution:**
- Ensure status badge is prominently displayed in card header
- Make status badge larger and more visible
- Position it at top-right of card

### 4. Incorrect Stats Values
**Problem:** Stats showing wrong counts for pending/confirmed
**Solution:**
- Stats should filter from `reservations` array BEFORE applying search/filters
- Need to count from raw data, not filtered results
- Issue: Currently using `reservations` but filters (status, date, table) are already applied in fetchData

### 5. Heavy Component - Needs Separation
**Problem:** Page and styles file are too large and complex
**Solution:** Break into components:

#### New Component Structure:
```
/components/admin/reservations/
├── ReservationsHeader.tsx          (Title, view toggle, refresh button)
├── ReservationsHeader.module.css
├── ReservationsFilters.tsx         (Search, status, date, table filters)
├── ReservationsFilters.module.css
├── ReservationsStats.tsx           (Stats cards)
├── ReservationsStats.module.css
├── ReservationsActions.tsx         (Export & bulk action buttons)
├── ReservationsActions.module.css
├── ReservationCard.tsx             (Individual card with all logic)
├── ReservationCard.module.css
├── ReservationsList.tsx            (Grid of cards)
├── ReservationsList.module.css
├── ReservationCalendar.tsx         (Already exists)
└── ReservationCalendar.module.css  (Already exists)
```

#### Main Page Will:
- Import and orchestrate components
- Manage state
- Handle API calls
- Pass data down to components

## Implementation Order

1. ✅ Fix stats calculation logic
2. ✅ Fix calendar status colors
3. ✅ Improve card styling (remove checkbox, bigger fonts, status colors)
4. ✅ Ensure status badge is visible in cards
5. ✅ Create component files (Header, Filters, Stats, Actions, Card, List)
6. ✅ Extract styles to component-specific CSS modules
7. ✅ Refactor main page to use new components
8. ✅ Test all functionality

## Status Color Scheme
- Pending: #f59e0b (Orange)
- Confirmed: #10b981 (Green)
- Cancelled: #ef4444 (Red)
- Completed: #6b7280 (Gray)
- No Show: #dc2626 (Dark Red)
