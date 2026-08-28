# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

Primary user is the developer/learner building and testing this project — not an external end-user audience. The app is used to exercise and demonstrate an SSO login flow against a companion .NET identity server while developing.

## Product Purpose

A minimal reference client for a custom .NET single-sign-on (SSO) identity server. It demonstrates the client side of a cookie-based, passwordless login flow: redirect to the identity server, receive an authenticated session cookie, fetch the current user, and log out.

## Positioning

A clean, small reference implementation of SSO client integration (React + Vite talking to a .NET identity server via `/auth/login`, `/auth/me`, `/auth/logout`, `/auth/health`) — not a production product in its own right.

## Operating Context

- React 19 + Vite + TypeScript + react-router-dom, dev server via `npm run dev`.
- Talks to an identity server expected at `http://localhost:5200` (`src/API.TS`): `GET /auth/me`, `GET /auth/health`, `POST /auth/logout`, and browser redirect to `GET /auth/login`.
- Two routes: `/` (Home — triggers login) and `/dashboard` (shows the authenticated user and a logout action).
- Session is cookie-based (`withCredentials: true`); there is no password field or local credential form anywhere in the client.

## Capabilities and Constraints

- Confirmed: login is a full-page redirect to the identity server, not an in-app form; session state is derived by calling `/auth/me`.
- Open/undecided: UI language (currently French), visual styling, and exact auth flow details are all explicitly open to change per the user — nothing here is a binding constraint.
- Known gap at time of writing: `src/API.TS` imports `axios`, which is not listed in `package.json` dependencies — worth resolving before relying on the dev server.

## Product Principles

- Keep the client thin: it never handles credentials directly, only redirects and reads session state from the identity server.
- Optimize for legibility as a reference/demo, not for production hardening or scale.
- Prefer the smallest change that keeps the login → dashboard → logout flow demonstrably working end to end.
