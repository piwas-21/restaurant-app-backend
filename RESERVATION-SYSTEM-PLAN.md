# Restaurant Reservation System - Implementation Plan

## Overview
Complete reservation system allowing customers to book tables and admins to manage reservations with email notifications and interactive table management.

## Restaurant Layout Details
### Indoor Tables
- Table 1: At entrance (near outdoor terrace)
- Table 2: Parallel to Table 1, near terrace
- Tables 3-10: Indoor seating area

### Outdoor Tables (Terrace)
- From entrance door:
  - 11a/11b: Side by side, 2 seats each
  - 12a/12b: Side by side, 2 seats each
  - 13a/13b: Side by side, 2 seats each
  - 14a/14b: Side by side, 2 seats each

---

## Backend Tasks

### Phase 1: Database & Domain Layer

#### 1.1 Create Domain Entities
- [ ] Create `Table` entity
  - Id (Guid)
  - TableNumber (string) - e.g., "1", "11a", "11b"
  - MaxGuests (int)
  - IsActive (bool)
  - IsOutdoor (bool)
  - PositionX (decimal) - for visual layout
  - PositionY (decimal) - for visual layout
  - Width (decimal) - for visual representation
  - Height (decimal) - for visual representation
  - BaseEntity fields (CreatedAt, UpdatedAt, etc.)

- [ ] Create `Reservation` entity
  - Id (Guid)
  - CustomerId (Guid?) - nullable for guest reservations
  - CustomerName (string)
  - CustomerEmail (string)
  - CustomerPhone (string)
  - TableId (Guid)
  - ReservationDate (DateTime)
  - StartTime (TimeSpan)
  - EndTime (TimeSpan)
  - NumberOfGuests (int)
  - Status (ReservationStatus enum: Pending, Confirmed, Cancelled, Completed)
  - SpecialRequests (string?)
  - Notes (string?) - Admin notes
  - BaseEntity fields

- [ ] Create `ReservationStatus` enum
  - Pending
  - Confirmed
  - Cancelled
  - Completed
  - NoShow

#### 1.2 Database Configuration
- [ ] Create `TableConfiguration` (EF Core)
- [ ] Create `ReservationConfiguration` (EF Core)
- [ ] Add DbSets to ApplicationDbContext
- [ ] Create and run migration

### Phase 2: Email Service

#### 2.1 Email Templates
- [ ] Create email template interface
- [ ] Create reservation confirmation email template (HTML)
- [ ] Create reservation approval email template (HTML)
- [ ] Create reservation cancellation email template (HTML)
- [ ] Create admin notification email template (HTML)
- [ ] Support multilingual email templates

#### 2.2 Email Service Implementation
- [ ] Create `IEmailService` interface
- [ ] Implement `EmailService` using SMTP (or SendGrid/AWS SES)
- [ ] Add email configuration to appsettings.json
- [ ] Create email sending methods:
  - SendReservationConfirmation
  - SendReservationApproval
  - SendReservationCancellation
  - SendAdminNotification

### Phase 3: API Endpoints - Tables

#### 3.1 Table Management (Admin)
- [ ] POST `/api/tables` - Create table
- [ ] PUT `/api/tables/{id}` - Update table
- [ ] DELETE `/api/tables/{id}` - Delete table (soft delete)
- [ ] PUT `/api/tables/{id}/position` - Update table position
- [ ] GET `/api/tables` - Get all tables (with filters)
- [ ] GET `/api/tables/{id}` - Get table by ID

#### 3.2 Table DTOs
- [ ] Create `TableDto`
- [ ] Create `CreateTableDto`
- [ ] Create `UpdateTableDto`
- [ ] Create `UpdateTablePositionDto`

#### 3.3 Commands & Queries (CQRS)
- [ ] CreateTableCommand
- [ ] UpdateTableCommand
- [ ] DeleteTableCommand
- [ ] UpdateTablePositionCommand
- [ ] GetTablesQuery
- [ ] GetTableByIdQuery

#### 3.4 Validators
- [ ] CreateTableCommandValidator
- [ ] UpdateTableCommandValidator
- [ ] UpdateTablePositionCommandValidator

### Phase 4: API Endpoints - Reservations

#### 4.1 Customer Endpoints
- [ ] GET `/api/reservations/availability` - Check available time slots
- [ ] POST `/api/reservations` - Create reservation
- [ ] GET `/api/reservations/my-reservations` - Get user's reservations
- [ ] PUT `/api/reservations/{id}/cancel` - Cancel reservation
- [ ] GET `/api/reservations/{id}` - Get reservation details

#### 4.2 Admin Endpoints
- [ ] GET `/api/reservations` - Get all reservations (with filters)
- [ ] PUT `/api/reservations/{id}/approve` - Approve reservation
- [ ] PUT `/api/reservations/{id}/reject` - Reject reservation
- [ ] PUT `/api/reservations/{id}` - Update reservation
- [ ] PUT `/api/reservations/{id}/notes` - Add admin notes
- [ ] GET `/api/reservations/calendar` - Get reservations for calendar view

#### 4.3 Reservation DTOs
- [ ] Create `ReservationDto`
- [ ] Create `CreateReservationDto`
- [ ] Create `UpdateReservationDto`
- [ ] Create `ReservationAvailabilityDto`
- [ ] Create `TimeSlotDto`
- [ ] Create `ApproveReservationDto`
- [ ] Create `RejectReservationDto`

#### 4.4 Commands & Queries (CQRS)
- [ ] CreateReservationCommand (with email notification)
- [ ] UpdateReservationCommand
- [ ] CancelReservationCommand (with email notification)
- [ ] ApproveReservationCommand (with email notification)
- [ ] RejectReservationCommand (with email notification)
- [ ] GetReservationsQuery
- [ ] GetReservationByIdQuery
- [ ] GetUserReservationsQuery
- [ ] GetAvailabilityQuery

#### 4.5 Validators
- [ ] CreateReservationCommandValidator
- [ ] UpdateReservationCommandValidator

#### 4.6 Business Logic
- [ ] Create `ReservationService` for availability checks
- [ ] Implement time slot calculation logic
- [ ] Implement table availability validation
- [ ] Implement business hours validation
- [ ] Implement reservation conflict detection

### Phase 5: Seeding & Configuration

#### 5.1 Table Seeding
- [ ] Create `TableSeeder` with restaurant's table layout
- [ ] Seed indoor tables (1-10)
- [ ] Seed outdoor tables (11a-14b)
- [ ] Set correct positions for visual layout

#### 5.2 Configuration
- [ ] Add reservation settings to configuration:
  - Business hours
  - Reservation duration (default 2 hours)
  - Maximum advance booking days
  - Minimum advance booking hours

---

## Frontend Tasks

### Phase 1: Types & Interfaces

#### 1.1 TypeScript Types
- [ ] Create `src/types/reservation.ts`
  - Table interface
  - Reservation interface
  - ReservationStatus enum
  - TimeSlot interface
  - Availability interface

### Phase 2: Services

#### 2.1 API Services
- [ ] Create `src/services/tableService.ts`
  - getTables()
  - getTableById()
  - createTable()
  - updateTable()
  - deleteTable()
  - updateTablePosition()

- [ ] Create `src/services/reservationService.ts`
  - getAvailability()
  - createReservation()
  - getMyReservations()
  - getReservationById()
  - cancelReservation()
  - (Admin) getReservations()
  - (Admin) approveReservation()
  - (Admin) rejectReservation()
  - (Admin) updateReservation()

### Phase 3: Customer Reservation Flow

#### 3.1 Reservation Page Components
- [ ] Create `src/app/reservations/page.tsx` - Main reservation page
- [ ] Create `src/components/reservations/ReservationWizard.tsx` - Multi-step wizard
- [ ] Create `src/components/reservations/steps/DateTimeSelection.tsx`
- [ ] Create `src/components/reservations/steps/TableSelection.tsx`
- [ ] Create `src/components/reservations/steps/GuestDetailsForm.tsx`
- [ ] Create `src/components/reservations/steps/ReservationSummary.tsx`

#### 3.2 Table Selection Components
- [ ] Create `src/components/reservations/TableLayout.tsx` - Visual restaurant layout
- [ ] Create `src/components/reservations/TableCard.tsx` - Single table component
- [ ] Create `src/components/reservations/TableAvailabilityIndicator.tsx`

#### 3.3 My Reservations Page
- [ ] Create `src/app/reservations/my-reservations/page.tsx`
- [ ] Create `src/components/reservations/ReservationCard.tsx`
- [ ] Create `src/components/reservations/ReservationDetails.tsx`
- [ ] Create `src/components/reservations/CancelReservationModal.tsx`

#### 3.4 Styles
- [ ] Create `src/app/reservations/Reservations.module.css`
- [ ] Create `src/components/reservations/ReservationWizard.module.css`
- [ ] Create `src/components/reservations/TableLayout.module.css`
- [ ] Create `src/components/reservations/ReservationCard.module.css`
- [ ] Ensure dark/light mode compatibility

### Phase 4: Admin Reservation Management

#### 4.1 Admin Reservations Page
- [ ] Create `src/app/admin/reservations/page.tsx`
- [ ] Create `src/components/admin/reservations/ReservationsTable.tsx`
- [ ] Create `src/components/admin/reservations/ReservationFilters.tsx`
- [ ] Create `src/components/admin/reservations/ReservationDetailsModal.tsx`
- [ ] Create `src/components/admin/reservations/ReservationCalendar.tsx`
- [ ] Create `src/components/admin/reservations/ApproveReservationModal.tsx`
- [ ] Create `src/components/admin/reservations/RejectReservationModal.tsx`

#### 4.2 Admin Table Management
- [ ] Create `src/app/admin/tables/page.tsx`
- [ ] Create `src/components/admin/tables/TablesList.tsx`
- [ ] Create `src/components/admin/tables/CreateTableModal.tsx`
- [ ] Create `src/components/admin/tables/EditTableModal.tsx`
- [ ] Create `src/components/admin/tables/TableLayoutEditor.tsx` - Drag & drop
- [ ] Create `src/components/admin/tables/DraggableTable.tsx`
- [ ] Create `src/components/admin/tables/TableForm.tsx`

#### 4.3 Styles
- [ ] Create `src/app/admin/reservations/AdminReservations.module.css`
- [ ] Create `src/app/admin/tables/AdminTables.module.css`
- [ ] Create `src/components/admin/reservations/ReservationsTable.module.css`
- [ ] Create `src/components/admin/tables/TableLayoutEditor.module.css`

### Phase 5: Navigation & Integration

#### 5.1 Navigation Updates
- [ ] Add "Reservations" link to main navigation
- [ ] Add "My Reservations" link to user menu
- [ ] Add "Reservations" section to admin sidebar
- [ ] Add "Tables" section to admin sidebar

#### 5.2 Homepage Integration
- [ ] Add reservation CTA section to homepage
- [ ] Link to reservation page

### Phase 6: Internationalization

#### 6.1 Translation Keys
- [ ] Add reservation translations to `src/locales/en.json`
- [ ] Add reservation translations to `src/locales/tr.json`
- [ ] Add reservation translations to `src/locales/de.json`
- [ ] Add reservation translations to `src/locales/fr.json`
- [ ] Add reservation translations to `src/locales/it.json`
- [ ] Add reservation translations to `src/locales/ar.json`
- [ ] Add reservation translations to `src/locales/es.json`

#### 6.2 Translation Categories
- Reservation wizard steps
- Table selection
- Guest details form
- Reservation status messages
- Email notifications (if shown in UI)
- Admin reservation management
- Admin table management
- Validation messages
- Success/error messages

### Phase 7: Utilities & Helpers

#### 7.1 Utility Functions
- [ ] Create `src/utils/reservationUtils.ts`
  - formatReservationTime()
  - getReservationStatus()
  - canCancelReservation()
  - generateTimeSlots()
  - calculateTablePosition()

#### 7.2 Validation
- [ ] Create reservation form validation schemas (Zod)
- [ ] Create table form validation schemas (Zod)

---

## Testing Checklist

### Backend Testing
- [ ] Test table CRUD operations
- [ ] Test reservation creation
- [ ] Test availability checking
- [ ] Test email sending
- [ ] Test reservation approval workflow
- [ ] Test conflict detection
- [ ] Test business hours validation

### Frontend Testing
- [ ] Test reservation wizard flow
- [ ] Test table selection
- [ ] Test availability display
- [ ] Test my reservations page
- [ ] Test admin reservation management
- [ ] Test admin table management
- [ ] Test drag & drop table positioning
- [ ] Test dark/light mode compatibility
- [ ] Test all language translations
- [ ] Test responsive design

---

## Implementation Order

### Sprint 1: Backend Foundation (Days 1-2)
1. Create domain entities
2. Database configuration and migration
3. Table API endpoints and seeding
4. Basic reservation API endpoints

### Sprint 2: Email & Business Logic (Day 3)
1. Email service setup
2. Email templates
3. Reservation business logic (availability, conflicts)
4. Complete reservation API endpoints

### Sprint 3: Customer Frontend (Days 4-5)
1. Types and services
2. Reservation wizard
3. Table selection with visual layout
4. My reservations page
5. Translation files

### Sprint 4: Admin Frontend (Days 6-7)
1. Admin reservation management
2. Admin table management
3. Drag & drop table editor
4. Calendar view
5. Complete translations

### Sprint 5: Testing & Polish (Day 8)
1. End-to-end testing
2. Dark/light mode verification
3. Translation verification
4. Bug fixes
5. Performance optimization

---

## Notes

- Email service should use environment variables for credentials
- Consider rate limiting for reservation creation
- Add validation for business hours and holidays
- Consider adding waiting list feature for future
- Consider SMS notifications for future enhancement
- Implement proper error handling and user feedback
- Use optimistic updates where appropriate
- Add loading states for all async operations
