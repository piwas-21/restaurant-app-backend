export const getFullImageUrl = (url: string) => {
  if (url.startsWith("/images")) return url;

  if (url.startsWith("http")) {
    try {
      const imageUrl = new URL(url);
      if (imageUrl.hostname === "restaurant-admin-api.orderhub.ch") {
        imageUrl.searchParams.set("q", "100");
        imageUrl.searchParams.set("w", "1200");
        return imageUrl.toString();
      }
    } catch {
      // ignore
    }
    return url;
  }

  return "https://lh3.google.com/u/0/d/" + url;
};
