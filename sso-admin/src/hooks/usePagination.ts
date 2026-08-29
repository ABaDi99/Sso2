import { useState } from "react";

/**
 * Pagination côté client : découpe un tableau déjà chargé en pages.
 * Ne convient pas à Users.tsx, dont la pagination est côté serveur
 * (page/pageSize/total viennent de l'API) — voir <Pager /> pour la partie
 * rendu, elle, commune aux deux cas.
 */
export function usePagination<T>(items: T[], pageSize: number) {
  const [page, setPage] = useState(1);

  const pageCount = Math.max(1, Math.ceil(items.length / pageSize));
  // Ne reste jamais sur une page vidée par une suppression.
  const currentPage = Math.min(page, pageCount);

  const pageItems = items.slice(
    (currentPage - 1) * pageSize,
    currentPage * pageSize
  );

  return { page: currentPage, setPage, pageCount, pageItems };
}
