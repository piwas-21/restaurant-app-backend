This is a [Next.js](https://nextjs.org) project bootstrapped with [`create-next-app`](https://nextjs.org/docs/app/api-reference/cli/create-next-app).

## Getting Started

### Prerequisites

* Node.js (v18.x or later recommended)
* npm, yarn, or pnpm

### Setup

1.  **Clone the repository:**

    ```bash
    git clone https://gitlab.com/restaurant-app3282120/frontend.git
    cd frontend
    ```

2.  **Install dependencies:**

    ```bash
    npm install
    # or
    yarn install
    # or
    pnpm install
    ```

3.  **Create an environment file:**

    Create a file named `.env.local` in the root of the project and add the following line:

    ```
    NEXT_PUBLIC_API_URL=http://localhost:5221/api
    ```

    *Note: This URL assumes the backend is running on the default port `5221`. If your backend is running on a different port, update the URL accordingly.*

4.  **Run the development server:**

    ```bash
    npm run dev
    # or
    yarn dev
    # or
    pnpm dev
    ```

Open [http://localhost:3000](http://localhost:3000) with your browser to see the result.

## Running Tests

This project uses [Jest](https://jestjs.io/) for unit and component testing.

*   **Run all tests:**

    ```bash
    npm test
    ```

*   **Run a specific test file:**

    ```bash
    npm test -- <path-to-test-file>
    ```

    For example, to run the tests for the authentication services, use:

    ```bash
    npm test -- src/lib/auth/utils.test.ts
    ```

*   **Run tests in watch mode:**

    ```bash
    npm test -- --watch
    ```
