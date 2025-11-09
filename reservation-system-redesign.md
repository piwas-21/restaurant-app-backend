1. Technical Fixes for Table Layout

Task: Fix the current table layout on the reservation page.

Goals:

Prevent overlapping tables (currently they stack visually).

Ensure responsive scaling (tables reposition correctly on different screen sizes).

Use absolute or grid positioning inside a relatively positioned container.

Each table should have its own defined coordinates (x, y) relative to the layout area.

Implementation Steps:

Represent tables as objects in a JSON structure with { id, x, y, width, height, seats, status }.

Render tables dynamically with inline styles using these coordinates.

Ensure hover/selection state styling doesn’t affect layout.

Maintain color codes and stylings compatible with app's overall dark and light theme design

Allow future dynamic updates — e.g. position data fetched from backend or admin config.

🪑 2. Redesign Layout Based on Real Floor Plan

Task: Redesign the restaurant seating layout to match the real floor plan (attached reference).

Details:

Indoor tables:

Tables 1–10 arranged as shown in the floor plan:

Tables 1–2 near the top (parallel, terrace/open air side).

Tables 3–5 along the left wall.

Table 6 in the bottom-left corner.

Tables 7–8 in the middle area.

Tables 9–10 along the right wall.

Outdoor tables:

Located outside the entrance area (bottom side).

Pairs of small two-person tables:

11a/11b closest to the door.

12a/12b next to them.

13a/13b next.

14a/14b farthest.

Outdoor tables are horizontally aligned in pairs.

🎨 3. UI/UX & Design Improvements

Add these refinements:

Task: Improve the overall design and user experience of the table selection view.

Requirements:

Add a background outline of the restaurant area (simple box or SVG floor zone).

Include labels: “Indoor Area” and “Outdoor Area”.

Animate selection with a small scale/shine effect when clicked.

Center the layout in the viewport with responsive scaling.

Use a subtle drop shadow or border for available tables for better contrast.

🧠 4. Example JSON Data Structure for Tables

Ask Claude-Code to start from this:

[
  { "id": "1", "x": 80, "y": 40, "shape": "circle", "status": "available" },
  { "id": "2", "x": 160, "y": 40, "shape": "circle", "status": "booked" },
  { "id": "3", "x": 40, "y": 120, "shape": "circle", "status": "available" },
  { "id": "4", "x": 40, "y": 180, "shape": "circle", "status": "available" },
  { "id": "5", "x": 40, "y": 240, "shape": "circle", "status": "available" },
  { "id": "6", "x": 40, "y": 320, "shape": "circle", "status": "available" },
  { "id": "7", "x": 140, "y": 160, "shape": "circle", "status": "available" },
  { "id": "8", "x": 140, "y": 220, "shape": "circle", "status": "available" },
  { "id": "9", "x": 260, "y": 120, "shape": "circle", "status": "available" },
  { "id": "10", "x": 260, "y": 180, "shape": "circle", "status": "available" },
  { "id": "11a", "x": 100, "y": 400, "shape": "square", "status": "available" },
  { "id": "11b", "x": 160, "y": 400, "shape": "square", "status": "available" },
  { "id": "12a", "x": 220, "y": 400, "shape": "square", "status": "available" },
  { "id": "12b", "x": 280, "y": 400, "shape": "square", "status": "available" },
  { "id": "13a", "x": 340, "y": 400, "shape": "square", "status": "available" },
  { "id": "13b", "x": 400, "y": 400, "shape": "square", "status": "available" },
  { "id": "14a", "x": 460, "y": 400, "shape": "square", "status": "available" },
  { "id": "14b", "x": 520, "y": 400, "shape": "square", "status": "available" }
]

💬 5. Bonus Task: Admin Layout Editor (Optional)

If you want future flexibility, you can also tell Claude-Code:

Add an admin layout editor where the owner can:

Drag tables to reposition.

Save layout coordinates to the backend.

Toggle indoor/outdoor areas.

Set seat count per table.
