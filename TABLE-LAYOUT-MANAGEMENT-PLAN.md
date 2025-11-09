# Table Layout Management Feature - Implementation Plan

## Overview
This feature will allow restaurant admins to visually manage and customize the table layout for the reservation system through a drag-and-drop interface.

## Current State
- Tables are positioned using X/Y coordinates stored in the database
- Frontend displays tables based on these coordinates with normalization
- No UI exists for admins to modify table positions
- Tables can only be created/edited with default positioning

## Goals
1. Provide a visual drag-and-drop editor for table positioning
2. Allow admins to save custom table layouts
3. Support both indoor and outdoor seating areas
4. Maintain responsive design across different screen sizes
5. Preview layout before saving changes

## Technical Requirements

### Backend Updates
**No changes needed** - The backend already supports storing positionX, positionY, width, and height for each table through the update API.

### Frontend Components

#### 1. Table Layout Editor Component (`/src/components/admin/tables/TableLayoutEditor.tsx`)

**Features:**
- Drag-and-drop canvas showing all tables
- Grid/snap-to-grid functionality for aligned placement
- Visual representation of table shapes (round/rectangular)
- Zoom in/out controls
- Undo/redo functionality
- Save/Cancel buttons

**State Management:**
```typescript
interface EditorState {
  tables: TableDto[];
  selectedTableId: string | null;
  isDragging: boolean;
  zoomLevel: number;
  hasUnsavedChanges: boolean;
  history: TableDto[][];
  historyIndex: number;
}
```

**Key Functions:**
- `handleTableDragStart(tableId: string)` - Initialize drag operation
- `handleTableDrag(tableId: string, x: number, y: number)` - Update position during drag
- `handleTableDragEnd(tableId: string)` - Finalize position
- `saveLayout()` - Persist changes to backend
- `resetLayout()` - Revert to saved state
- `undo()` / `redo()` - History navigation

#### 2. Table Layout Page (`/src/app/admin/table-layout/page.tsx`)

**URL:** `/admin/table-layout`

**Features:**
- Navigation link from admin dashboard
- Two-panel layout: editor canvas + table list sidebar
- Filter tables by indoor/outdoor
- Settings panel for canvas size and grid options

#### 3. Navigation Updates

Add new menu item to admin navigation:
```typescript
{
  name: 'Table Layout',
  href: '/admin/table-layout',
  icon: LayoutIcon
}
```

## Implementation Phases

### Phase 1: Basic Editor UI (Day 1)
- [ ] Create TableLayoutEditor component
- [ ] Add canvas with table rendering
- [ ] Implement table selection (click to select)
- [ ] Add basic styling and visual feedback
- [ ] Create admin/table-layout page
- [ ] Add navigation menu item

### Phase 2: Drag & Drop (Day 2)
- [ ] Implement drag-and-drop using HTML5 Drag API or React DnD
- [ ] Add position constraints (stay within canvas)
- [ ] Update table position state during drag
- [ ] Visual feedback while dragging (shadow, outline)
- [ ] Collision detection (optional: prevent overlap)

### Phase 3: Grid & Alignment (Day 2)
- [ ] Add grid overlay option
- [ ] Snap-to-grid functionality
- [ ] Alignment guides when dragging
- [ ] Auto-align tools (align left, center, distribute evenly)

### Phase 4: Save & Persistence (Day 3)
- [ ] Connect to backend update API
- [ ] Batch update all table positions
- [ ] Loading states and error handling
- [ ] Success/failure notifications
- [ ] Unsaved changes warning on navigation

### Phase 5: History & Controls (Day 3)
- [ ] Undo/redo functionality
- [ ] Zoom controls (+/- buttons, slider)
- [ ] Pan canvas (drag background to move view)
- [ ] Reset to default layout option
- [ ] Export/import layout (optional JSON format)

### Phase 6: Polish & UX (Day 4)
- [ ] Keyboard shortcuts (Ctrl+Z, Ctrl+Y, Delete, Arrow keys)
- [ ] Table properties panel (edit size, capacity on selection)
- [ ] Preview mode (see layout as customers will see it)
- [ ] Responsive design for mobile/tablet
- [ ] Accessibility improvements (ARIA labels, keyboard navigation)

## Technical Considerations

### Coordinate System
- Use percentage-based positioning (0-100%) for responsive layouts
- Store in database as decimal values (e.g., 25.5, 30.0)
- Convert to pixels for rendering based on canvas size

### Drag & Drop Libraries
**Option 1: react-dnd** (Full-featured)
- Pros: Powerful, well-maintained, handles complex scenarios
- Cons: Larger bundle size, steeper learning curve

**Option 2: @dnd-kit** (Modern, lightweight)
- Pros: Better performance, smaller bundle, modern API
- Cons: Newer, fewer examples

**Option 3: Native HTML5** (No dependencies)
- Pros: No extra dependencies, simple
- Cons: More manual work, browser inconsistencies

**Recommendation:** Use @dnd-kit for best balance of features and performance

### State Management
- Use React useState for local editor state
- Optimistic updates for better UX
- Debounce auto-save (optional feature)

### Performance
- Memoize table components to prevent unnecessary re-renders
- Use React.memo() for table items
- Virtualization if supporting 100+ tables

## API Integration

### Update Single Table Position
```typescript
PUT /api/tables/{id}
Body: {
  positionX: 45.5,
  positionY: 30.0,
  // ... other fields
}
```

### Batch Update (Optional Backend Feature)
```typescript
PUT /api/tables/bulk-update-positions
Body: {
  updates: [
    { id: "table-1", positionX: 10, positionY: 20 },
    { id: "table-2", positionX: 30, positionY: 20 }
  ]
}
```

## UI/UX Design

### Canvas
- Light gray background with subtle grid
- Tables render with same styling as reservation page
- Selected table: highlighted border (golden yellow)
- Hover: cursor changes to 'move'

### Sidebar
- List of all tables (scrollable)
- Click to select and focus on canvas
- Filter: Indoor / Outdoor / All
- Add new table button

### Toolbar
- Zoom: [−] 75% [+]
- Grid: [x] Snap to Grid
- Undo / Redo buttons
- Save / Cancel buttons

### Keyboard Shortcuts
- `Ctrl+Z`: Undo
- `Ctrl+Y` / `Ctrl+Shift+Z`: Redo
- `Delete`: Remove selected table (with confirmation)
- `Arrow Keys`: Move selected table by 1% (or 5% with Shift)
- `Ctrl+S`: Save layout
- `Esc`: Deselect / Cancel

## Success Metrics
- Admins can reposition tables in under 30 seconds
- Changes persist correctly in database
- Layout appears identically on customer reservation page
- No performance degradation with 50+ tables
- Responsive on tablets (minimum 768px width)

## Future Enhancements (Post-MVP)
- Multiple saved layouts (breakfast, lunch, dinner, events)
- Copy table / duplicate selected
- Rotate tables (45°, 90°, etc.)
- Custom table shapes (beyond round/rectangular)
- Background image upload (floor plan overlay)
- Accessibility zones visualization
- Heatmap showing popular tables
- Integration with real-time reservation availability

## Testing Checklist
- [ ] Tables can be dragged and dropped
- [ ] Positions save correctly to database
- [ ] Layout matches on reservation page after save
- [ ] Undo/redo works correctly (5+ operations)
- [ ] Grid snap works as expected
- [ ] Zoom maintains table positions accurately
- [ ] Concurrent editing handling (two admins editing)
- [ ] Validation: tables stay within canvas bounds
- [ ] Performance with maximum expected tables (50+)
- [ ] Cross-browser testing (Chrome, Firefox, Safari, Edge)
- [ ] Mobile/tablet responsive behavior

## Estimated Timeline
- **Total Effort:** 3-4 days
- **Phase 1-2:** 2 days (Core editor + drag-and-drop)
- **Phase 3-4:** 1 day (Grid, alignment, save)
- **Phase 5-6:** 1 day (Polish, history, UX)
- **Testing & Refinement:** 0.5 day

## Dependencies
- `@dnd-kit/core` - Drag and drop functionality
- `@dnd-kit/utilities` - Helper utilities for transforms
- Existing reservation service API
- Existing TableDto type definitions

## Notes
- Start with indoor tables only, then add outdoor toggle
- Coordinate with backend team for batch update API (optional but recommended)
- Consider adding "lock table position" feature to prevent accidental moves
- May want to add table rotation in future (0°, 45°, 90°, etc.)
